using System.Security.Cryptography;
using System.Text.Json;
using NexaConnect.Contracts.IntegrationEvents;
using NexaConnect.Services.POS.Application.DayClose;
using NexaConnect.Services.POS.Domain.DayClose;
using Npgsql;

namespace NexaConnect.Services.POS.Infrastructure.DayClose;

public sealed class PostgresDaySettlementStore(NpgsqlDataSource source) : IDaySettlementStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<SettlementState?> ReadAsync(DayIdentity day, CancellationToken ct)
    {
        await using var c = await source.OpenConnectionAsync(ct);
        return await Load(c, null, day, false, ct);
    }
    public async Task<SettlementState?> ReplayAsync(DayIdentity day,SettlementCommand command,string subject,CancellationToken ct)
    {
        await using var q=source.CreateCommand("SELECT state::text FROM branch_day_settlements WHERE organization_id=$1 AND state->'command'->>'operationId'=$2");
        Add(q,day.OrganizationId,command.OperationId.ToString("D"));var text=await q.ExecuteScalarAsync(ct) as string;
        if(text is null)return null;var state=JsonSerializer.Deserialize<SettlementState>(text,Json)!;
        if(state.Identity!=day||state.Command!=command||state.Subject!=subject)throw new DayCloseConflictException("settlement_operation_conflict");
        return state;
    }

    public async Task<SettlementState> BeginAsync(DayIdentity day, SettlementCommand command, FinalizationState preparation,
        ApprovalView approval, PreparationActor actor, Guid correlationId, DateTimeOffset now, CancellationToken ct)
    {
        if (correlationId == Guid.Empty || actor.DecisionId == Guid.Empty || string.IsNullOrWhiteSpace(actor.Subject)) throw new ArgumentException();
        await using var c = await source.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        await Run(c, tx, "SELECT pg_advisory_xact_lock(hashtextextended($1,0))", ct, "settlement:" + day.OrganizationId + ":" + command.OperationId);
        var bound = await PostgresFinalizationStore.ApprovalMatches(c, tx, day, approval, now, ct);
        await using var q = new NpgsqlCommand("SELECT state::text FROM branch_day_finalization_preparations WHERE organization_id=$1 AND restaurant_id=$2 AND branch_id=$3 AND business_date=$4 FOR UPDATE", c, tx);
        Scope(q, day);
        var text = await q.ExecuteScalarAsync(ct) as string;
        var current = text is null ? null : JsonSerializer.Deserialize<FinalizationState>(text, Json);
        var old = await Load(c, tx, day, true, ct);
        var fingerprint = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { day, command, actor.Subject }, Json)));
        await using var operation = new NpgsqlCommand("SELECT fingerprint FROM branch_day_settlement_operations WHERE organization_id=$1 AND operation_id=$2", c, tx);
        Add(operation, day.OrganizationId, command.OperationId);
        var existing = await operation.ExecuteScalarAsync(ct) as string;
        if (existing is not null)
        {
            if (existing != fingerprint || old?.Command != command || old.Subject != actor.Subject) throw new DayCloseConflictException("settlement_operation_conflict");
            await tx.CommitAsync(ct); return old;
        }
        if (old is not null && old.Status != "aborted") throw new DayCloseConflictException("settlement_exists");
        if (!bound || current is null || current.Version != preparation.Version || current.Identity != day)
            throw new DayCloseConflictException("reviewed_preparation_changed");
        BranchDaySettlement.ValidateAdmission(command, current, approval.Decision, approval.Version, approval.Status, now);
        var state = new SettlementState(Guid.NewGuid(), day, command, actor.Subject, actor.DecisionId, correlationId, current, "arming", null, null, [], NexaConnect.Observability.CorrelationContext.Current);
        await Run(c, tx, "INSERT INTO branch_day_settlement_operations(organization_id,operation_id,fingerprint,command) VALUES($1,$2,$3,$4::jsonb)", ct,
            day.OrganizationId, command.OperationId, fingerprint, JsonSerializer.Serialize(command, Json));
        await Run(c, tx, "INSERT INTO branch_day_settlements(organization_id,restaurant_id,branch_id,business_date,id,state) VALUES($1,$2,$3,$4,$5,$6::jsonb)", ct,
            day.OrganizationId, day.RestaurantId, day.BranchId, day.BusinessDate, state.Id, JsonSerializer.Serialize(state, Json));
        await Audit(c, tx, state, "intent", ct);
        await tx.CommitAsync(ct); return state;
    }

    public async Task<SettlementState> DecideAsync(SettlementState observed, bool commit, SettlementSource[] sources, DateTimeOffset now, CancellationToken ct)
    {
        await using var c = await source.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        var state = await LoadById(c, tx, observed.Identity.OrganizationId,observed.Id,ct) ?? throw new DayCloseConflictException("settlement_missing");
        if (state.Id != observed.Id) throw new DayCloseConflictException("settlement_changed");
        if (state.DecisionId is not null) { await tx.CommitAsync(ct); return state; }
        if (commit) ValidateProof(state, sources, "armed", null);
        var decision = Guid.NewGuid();
        var receipt = commit ? BranchDaySettlement.Commit(state.Id, decision, state.CorrelationId, state.Preparation, now) : null;
        state = state with { Status = commit ? "committing" : "aborting", DecisionId = decision, Receipt = receipt, Sources = sources };
        await Run(c, tx, "INSERT INTO branch_day_settlement_decisions(organization_id,settlement_id,decision_id,decision) VALUES($1,$2,$3,$4)", ct,
            state.Identity.OrganizationId, state.Id, decision, commit ? "commit" : "abort");
        if (receipt is not null)
        {
            await Run(c, tx, "INSERT INTO branch_day_settlement_receipts(organization_id,settlement_id,receipt) VALUES($1,$2,$3::jsonb)", ct,
                state.Identity.OrganizationId, state.Id, JsonSerializer.Serialize(receipt, Json));
            var snapshot = receipt.Snapshot;
            var seals=snapshot.Seals!;
            var publication = new BranchDaySettledV1(receipt.EventId, receipt.CorrelationId, receipt.SettledAtUtc, state.Id,
                state.Identity.OrganizationId, state.Identity.RestaurantId, state.Identity.BranchId, state.Identity.BusinessDate,
                receipt.ApprovalId, receipt.SealVersion, snapshot.Currency, snapshot.GrossSales, snapshot.CompletedRefunds, snapshot.NetSales,
                snapshot.CashVariance, snapshot.Tenders.Select(t=>new SettlementTenderV1(t.Method,t.Currency,t.Amount)).ToArray(),
                new[]{("Order",seals.Order),("Payment",seals.Payment),("POS",seals.Pos)}
                    .Select(s=>new SettlementSourceV1(s.Item1,s.Item2.SealId,s.Item2.RevisionEpoch,s.Item2.SourceRevision)).ToArray());
            await Run(c, tx, "INSERT INTO outbox_messages(id,event_type,contract_version,aggregate_type,aggregate_id,payload,correlation_id,occurred_at_utc) VALUES($1,'pos.branch-day-settled.v1',1,'branch-day-settlement',$2,$3::jsonb,$4,$5)", ct,
                publication.EventId, state.Id, JsonSerializer.Serialize(publication, Json), state.CorrelationId, now);
        }
        await Save(c, tx, state, ct); await Audit(c, tx, state, commit ? "commit" : "abort", ct);
        await tx.CommitAsync(ct); return state;
    }

    public async Task<SettlementState> AcknowledgeAsync(SettlementState observed, SettlementSource[] sources, CancellationToken ct)
    {
        await using var c = await source.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        var state = await LoadById(c, tx, observed.Identity.OrganizationId,observed.Id,ct) ?? throw new DayCloseConflictException("settlement_missing");
        if (state.Id != observed.Id || state.DecisionId != observed.DecisionId || state.DecisionId is null) throw new DayCloseConflictException("settlement_changed");
        ValidateProof(state, sources, state.Receipt is null ? "aborted" : "committed", state.DecisionId);
        state = state with { Status = state.Receipt is null ? "aborted" : "finalized", Sources = sources };
        await Save(c, tx, state, ct); await Audit(c, tx, state, "acknowledged", ct);
        await tx.CommitAsync(ct); return state;
    }

    public async Task<SettlementState[]> PendingAsync(CancellationToken ct)
    {
        await using var c = await source.OpenConnectionAsync(ct);
        await using var q = new NpgsqlCommand("WITH candidate AS (SELECT organization_id,id FROM branch_day_settlements WHERE state->>'status' NOT IN('finalized','aborted') ORDER BY last_attempt_at_utc,id FOR UPDATE SKIP LOCKED LIMIT 10) UPDATE branch_day_settlements s SET last_attempt_at_utc=clock_timestamp() FROM candidate c WHERE s.organization_id=c.organization_id AND s.id=c.id RETURNING s.state::text", c);
        await using var rows = await q.ExecuteReaderAsync(ct);
        var result = new List<SettlementState>();
        while (await rows.ReadAsync(ct)) result.Add(JsonSerializer.Deserialize<SettlementState>(rows.GetString(0), Json)!);
        return result.ToArray();
    }

    private static void ValidateProof(SettlementState state, SettlementSource[] sources, string phase, Guid? decision)
    {
        BranchDaySettlement.ValidateAcknowledgements(state.Preparation,sources.Select(s=>new SettlementAcknowledgement(s.Source,s.Proof.Command.Fence.SealId,s.Proof.Phase,s.Proof.DecisionId)).ToArray(),phase,decision);
        foreach (var owner in new[] { "Order", "Payment", "POS" })
        {
            var proof = sources.SingleOrDefault(s => s.Source == owner)?.Proof;
            var expected = SettlementBarrierCommand.Create(state, owner);
            if (proof is null || proof.Command != expected || proof.Phase != phase
                || phase != "armed" && proof.DecisionId != decision)
                throw new DayCloseConflictException("source_barriers_unproven");
        }
    }
    private static async Task<SettlementState?> Load(NpgsqlConnection c, NpgsqlTransaction? tx, DayIdentity day, bool locked, CancellationToken ct)
    {
        await using var q = new NpgsqlCommand("SELECT state::text FROM branch_day_settlements WHERE organization_id=$1 AND restaurant_id=$2 AND branch_id=$3 AND business_date=$4 ORDER BY (state->>'status'<>'aborted') DESC,last_attempt_at_utc DESC,id LIMIT 1" + (locked ? " FOR UPDATE" : ""), c, tx);
        Scope(q, day); var text = await q.ExecuteScalarAsync(ct) as string;
        return text is null ? null : JsonSerializer.Deserialize<SettlementState>(text, Json);
    }
    private static async Task<SettlementState?> LoadById(NpgsqlConnection c,NpgsqlTransaction tx,Guid organization,Guid id,CancellationToken ct)
    {
        await using var q=new NpgsqlCommand("SELECT state::text FROM branch_day_settlements WHERE organization_id=$1 AND id=$2 FOR UPDATE",c,tx);
        Add(q,organization,id);var text=await q.ExecuteScalarAsync(ct) as string;
        return text is null?null:JsonSerializer.Deserialize<SettlementState>(text,Json);
    }
    private static Task Save(NpgsqlConnection c, NpgsqlTransaction tx, SettlementState state, CancellationToken ct) =>
        Run(c, tx, "UPDATE branch_day_settlements SET state=$3::jsonb,last_attempt_at_utc=clock_timestamp() WHERE organization_id=$1 AND id=$2", ct, state.Identity.OrganizationId, state.Id, JsonSerializer.Serialize(state, Json));
    private static Task Audit(NpgsqlConnection c, NpgsqlTransaction tx, SettlementState state, string action, CancellationToken ct) =>
        Run(c, tx, "INSERT INTO branch_day_settlement_audit(organization_id,settlement_id,action,state) VALUES($1,$2,$3,$4::jsonb) ON CONFLICT DO NOTHING", ct, state.Identity.OrganizationId, state.Id, action, JsonSerializer.Serialize(state, Json));
    private static void Scope(NpgsqlCommand q, DayIdentity day) => Add(q, day.OrganizationId, day.RestaurantId, day.BranchId, day.BusinessDate);
    private static void Add(NpgsqlCommand q, params object[] values) { foreach (var value in values) q.Parameters.AddWithValue(value); }
    private static async Task Run(NpgsqlConnection c, NpgsqlTransaction tx, string sql, CancellationToken ct, params object[] values)
    { await using var q = new NpgsqlCommand(sql, c, tx) { CommandTimeout = 10 }; Add(q, values); await q.ExecuteNonQueryAsync(ct); }
}
