using System.Text.Json;
using Microsoft.Extensions.Options;
using NexaConnect.Contracts.IntegrationEvents;
using NexaConnect.Services.Payment.Application.Intents;
using NexaConnect.Services.Payment.Application.Refunds;
using NexaConnect.Services.Payment.Infrastructure.Providers;
using Npgsql;
using NpgsqlTypes;

namespace NexaConnect.Services.Payment.Infrastructure;

public sealed class PostgresPaymentRefunds(NpgsqlDataSource dataSource, IOptions<PaymentProviderOptions> options) : IPaymentRefunds
{
    private readonly TimeSpan leaseDuration = options.Value.LeaseDuration;
    private readonly int maximumAttempts = Math.Clamp(options.Value.MaximumRefundRecoveryAttempts, 1, 100);

    public PaymentRefundLease Begin(Guid organizationId, Guid paymentIntentId, CreatePaymentRefund command, PaymentMutationContext context)
    {
        Validate(command, context);
        using NpgsqlConnection connection = dataSource.OpenConnection();
        using NpgsqlTransaction transaction = connection.BeginTransaction();
        PaymentIntent intent = ReadIntent(connection, transaction, organizationId, paymentIntentId, true)
            ?? throw new KeyNotFoundException("Payment intent was not found.");
        if (intent.Status != "captured") throw new InvalidOperationException("Only a captured payment can be refunded.");
        if (!string.Equals(intent.Currency, command.Currency.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The refund currency must match the captured payment.");
        PaymentRefund? replay = ReadByOperation(connection, transaction, organizationId, paymentIntentId, command.OperationId);
        if (replay is not null)
        {
            if (replay.Amount != command.Amount || replay.Currency != command.Currency.Trim().ToUpperInvariant()
                || replay.ReasonCode != InMemoryPaymentRefunds.NormalizeReason(command.ReasonCode))
                throw new PaymentRefundConflictException("The operation identifier is already associated with a different refund request.");
            transaction.Commit(); return new(replay, intent, false);
        }
        using (var total = new NpgsqlCommand("SELECT COALESCE(sum(amount),0) FROM refunds WHERE payment_intent_id=$1 AND status IN ('processing','refund_unknown','review_required','completed')", connection, transaction))
        {
            total.Parameters.AddWithValue(paymentIntentId);
            if ((decimal)total.ExecuteScalar()! + command.Amount > intent.Amount)
                throw new InvalidOperationException("The refund would exceed the captured payment amount.");
        }
        DateTimeOffset now = DateTimeOffset.UtcNow; Guid id = Guid.NewGuid(); string reason = InMemoryPaymentRefunds.NormalizeReason(command.ReasonCode);
        using (var insert = new NpgsqlCommand("""
            INSERT INTO refunds(id,payment_intent_id,idempotency_key,amount,currency,reason_code,status,requested_at_utc,requested_by,updated_at_utc,
              organization_id,restaurant_id,branch_id,order_id,operation_id,authorization_decision_id,lease_owner,lease_expires_at_utc)
            VALUES($1,$2,$3,$4,$5,$6,'processing',$7,$8,$7,$9,$10,$11,$12,$13,$14,$8,$15)
            """, connection, transaction))
        {
            insert.Parameters.AddWithValue(id); insert.Parameters.AddWithValue(paymentIntentId); insert.Parameters.AddWithValue(command.OperationId.ToString("D"));
            insert.Parameters.AddWithValue(command.Amount); insert.Parameters.AddWithValue(command.Currency.Trim().ToUpperInvariant()); insert.Parameters.AddWithValue(reason);
            insert.Parameters.AddWithValue(now); insert.Parameters.AddWithValue(context.ActorSubjectId.Trim()); insert.Parameters.AddWithValue(organizationId);
            insert.Parameters.AddWithValue(intent.RestaurantId); insert.Parameters.AddWithValue(intent.BranchId); insert.Parameters.AddWithValue(intent.OrderId);
            insert.Parameters.AddWithValue(command.OperationId); insert.Parameters.AddWithValue(command.AuthorizationDecisionId); insert.Parameters.AddWithValue(now.Add(leaseDuration));
            insert.ExecuteNonQuery();
        }
        PaymentRefund refund = Read(connection, transaction, organizationId, id)!;
        Append(connection, transaction, refund, context, now, "payment.refund.requested",
            "payment.refund-requested.v1", new PaymentRefundRequestedV1(Guid.NewGuid(), context.CorrelationId, now,
                organizationId, intent.RestaurantId, intent.BranchId, intent.OrderId, intent.Id, refund.Id,
                refund.Amount, refund.Currency, refund.ReasonCode, command.AuthorizationDecisionId));
        transaction.Commit(); return new(refund, intent, true);
    }

    public PaymentRefund Complete(Guid organizationId, Guid refundId, long expectedVersion, ProviderRefundOutcome outcome,
        string? providerRefundId, string? failureCode, PaymentMutationContext context) =>
        Transition(organizationId, refundId, expectedVersion, outcome, providerRefundId, failureCode, context, false);
    public PaymentRefund Reconcile(Guid organizationId, Guid refundId, long expectedVersion, ProviderRefundOutcome outcome,
        string? providerRefundId, string? failureCode, PaymentMutationContext context) =>
        Transition(organizationId, refundId, expectedVersion, outcome, providerRefundId, failureCode, context, true);

    public PaymentRefund? Get(Guid organizationId, Guid refundId)
    { using NpgsqlConnection c = dataSource.OpenConnection(); return Read(c, null, organizationId, refundId); }
    public PaymentRefund? GetByOperation(Guid organizationId, Guid paymentIntentId, Guid operationId)
    { using NpgsqlConnection c=dataSource.OpenConnection(); using var command=new NpgsqlCommand(Select+" WHERE organization_id=$1 AND payment_intent_id=$2 AND operation_id=$3",c);command.Parameters.AddWithValue(organizationId);command.Parameters.AddWithValue(paymentIntentId);command.Parameters.AddWithValue(operationId);using NpgsqlDataReader r=command.ExecuteReader();return r.Read()?Map(r):null; }

    public IReadOnlyCollection<PaymentRefund> List(Guid organizationId, Guid paymentIntentId)
    {
        using NpgsqlConnection c = dataSource.OpenConnection();
        using var command = new NpgsqlCommand(Select + " WHERE organization_id=$1 AND payment_intent_id=$2 ORDER BY requested_at_utc DESC", c);
        command.Parameters.AddWithValue(organizationId); command.Parameters.AddWithValue(paymentIntentId);
        using NpgsqlDataReader reader = command.ExecuteReader(); var result = new List<PaymentRefund>(); while (reader.Read()) result.Add(Map(reader)); return result;
    }

    public PaymentRefundLease ClaimExpired(Guid organizationId, Guid refundId, PaymentMutationContext context)
    {
        ValidateContext(context); DateTimeOffset now = DateTimeOffset.UtcNow;
        using NpgsqlConnection c = dataSource.OpenConnection(); using NpgsqlTransaction t = c.BeginTransaction();
        PaymentRefund refund = Read(c, t, organizationId, refundId, true) ?? throw new KeyNotFoundException("Refund was not found.");
        PaymentIntent intent = ReadIntent(c, t, organizationId, refund.PaymentIntentId, false)!;
        if (refund.Status == "processing" && refund.LeaseExpiresAtUtc > now) { t.Commit(); return new(refund, intent, false); }
        if (refund.Status is not ("processing" or "refund_unknown")) { t.Commit(); return new(refund, intent, false); }
        int attempts = refund.RecoveryAttemptCount + 1;
        string status = attempts > maximumAttempts ? "review_required" : "processing";
        using (var update = new NpgsqlCommand("UPDATE refunds SET status=$1,failure_code=CASE WHEN $1='review_required' THEN 'refund_recovery_exhausted' ELSE failure_code END,lease_owner=CASE WHEN $1='processing' THEN $2 ELSE NULL END,lease_expires_at_utc=CASE WHEN $1='processing' THEN $3 ELSE NULL END,recovery_attempt_count=$4,updated_at_utc=$5,concurrency_version=concurrency_version+1 WHERE id=$6 AND concurrency_version=$7", c, t))
        { update.Parameters.AddWithValue(status); update.Parameters.AddWithValue(context.ActorSubjectId.Trim()); update.Parameters.AddWithValue(now.Add(leaseDuration)); update.Parameters.AddWithValue(attempts); update.Parameters.AddWithValue(now); update.Parameters.AddWithValue(refundId); update.Parameters.AddWithValue(refund.ConcurrencyVersion); update.ExecuteNonQuery(); }
        PaymentRefund claimed = Read(c, t, organizationId, refundId, true)!;
        if (status == "review_required") Append(c, t, claimed, context, now, "payment.refund.review-required", "payment.refund-review-required.v1",
            new PaymentRefundReviewRequiredV1(Guid.NewGuid(), context.CorrelationId, now, organizationId, claimed.OrderId, claimed.PaymentIntentId, claimed.Id, claimed.FailureCode!));
        t.Commit(); return new(claimed, intent, status == "processing");
    }

    public IReadOnlyCollection<PaymentRefund> FindRecoverable()
    {
        using NpgsqlConnection c = dataSource.OpenConnection(); using var command = new NpgsqlCommand(Select + " WHERE status='refund_unknown' OR (status='processing' AND lease_expires_at_utc<=now()) ORDER BY requested_at_utc LIMIT 100", c);
        using NpgsqlDataReader reader = command.ExecuteReader(); var result = new List<PaymentRefund>(); while (reader.Read()) result.Add(Map(reader)); return result;
    }

    private PaymentRefund Transition(Guid organizationId, Guid refundId, long expectedVersion, ProviderRefundOutcome outcome,
        string? providerRefundId, string? failureCode, PaymentMutationContext context, bool recovery)
    {
        ValidateContext(context);
        if (outcome == ProviderRefundOutcome.Refunded && !SafeReference(providerRefundId)) throw new ArgumentException("A completed refund requires a valid provider reference.");
        DateTimeOffset now = DateTimeOffset.UtcNow; using NpgsqlConnection c = dataSource.OpenConnection(); using NpgsqlTransaction t = c.BeginTransaction();
        PaymentRefund refund = Read(c, t, organizationId, refundId, true) ?? throw new KeyNotFoundException("Refund was not found.");
        if (refund.Status == "completed") { t.Commit(); return refund; }
        if (refund.Status != "processing" || refund.ConcurrencyVersion != expectedVersion) throw new PaymentConcurrencyException("The refund changed while provider processing was in progress.");
        PaymentIntent intent = ReadIntent(c, t, organizationId, refund.PaymentIntentId, true)!;
        string status = outcome == ProviderRefundOutcome.Refunded ? "completed" : outcome == ProviderRefundOutcome.Failed ? "failed" : "refund_unknown";
        decimal cumulative = 0; string? receiptJson = null;
        if (outcome == ProviderRefundOutcome.Refunded)
        {
            using var sum = new NpgsqlCommand("SELECT COALESCE(sum(amount),0) FROM refunds WHERE payment_intent_id=$1 AND status='completed'", c, t); sum.Parameters.AddWithValue(refund.PaymentIntentId);
            cumulative = (decimal)sum.ExecuteScalar()! + refund.Amount;
            receiptJson = JsonSerializer.Serialize(new PaymentRefundReceipt($"RF-{refund.Id:N}".ToUpperInvariant(), refund.Id,
                refund.PaymentIntentId, refund.OrderId, refund.Amount, refund.Currency, refund.ReasonCode, now, intent.Amount, cumulative));
        }
        string? failure = outcome == ProviderRefundOutcome.Refunded ? null : failureCode ?? (recovery ? "provider_refund_status_unknown" : "provider_refund_failed");
        using (var update = new NpgsqlCommand("UPDATE refunds SET status=$1,provider_refund_id=$2,failure_code=$3,completed_at_utc=CASE WHEN $1='completed' THEN $4 ELSE NULL END,failed_at_utc=CASE WHEN $1='failed' THEN $4 ELSE NULL END,receipt_snapshot=$5::jsonb,lease_owner=NULL,lease_expires_at_utc=NULL,last_reconciled_at_utc=CASE WHEN $6 THEN $4 ELSE last_reconciled_at_utc END,updated_at_utc=$4,concurrency_version=concurrency_version+1 WHERE id=$7 AND concurrency_version=$8", c, t))
        { update.Parameters.AddWithValue(status); update.Parameters.Add(new NpgsqlParameter { NpgsqlDbType=NpgsqlDbType.Text,Value=(object?)(outcome == ProviderRefundOutcome.Refunded ? providerRefundId!.Trim() : null)??DBNull.Value }); update.Parameters.Add(new NpgsqlParameter { NpgsqlDbType=NpgsqlDbType.Text,Value=(object?)failure??DBNull.Value }); update.Parameters.AddWithValue(now); update.Parameters.Add(new NpgsqlParameter { NpgsqlDbType=NpgsqlDbType.Jsonb,Value=(object?)receiptJson??DBNull.Value }); update.Parameters.AddWithValue(recovery); update.Parameters.AddWithValue(refundId); update.Parameters.AddWithValue(expectedVersion); if (update.ExecuteNonQuery()!=1) throw new PaymentConcurrencyException("The refund changed while provider processing completed."); }
        PaymentRefund result = Read(c, t, organizationId, refundId, true)!;
        string action = outcome == ProviderRefundOutcome.Refunded ? (recovery ? "payment.refund.reconciled" : "payment.refund.succeeded") : outcome == ProviderRefundOutcome.Failed ? "payment.refund.failed" : "payment.refund.uncertain";
        string type = outcome == ProviderRefundOutcome.Refunded ? "payment.refunded.v1" : outcome == ProviderRefundOutcome.Failed ? "payment.refund-failed.v1" : "payment.refund-uncertain.v1";
        IIntegrationEvent evt = outcome == ProviderRefundOutcome.Refunded
            ? new PaymentRefundedV1(Guid.NewGuid(), context.CorrelationId, now, organizationId, result.RestaurantId, result.BranchId, result.OrderId, result.PaymentIntentId, result.Id, result.Amount, result.Currency, result.ReasonCode, cumulative, intent.Amount, result.Receipt!.ReceiptNumber)
            : outcome == ProviderRefundOutcome.Failed ? new PaymentRefundFailedV1(Guid.NewGuid(), context.CorrelationId, now, organizationId, result.OrderId, result.PaymentIntentId, result.Id, result.FailureCode!)
            : new PaymentRefundUncertainV1(Guid.NewGuid(), context.CorrelationId, now, organizationId, result.OrderId, result.PaymentIntentId, result.Id, result.FailureCode!);
        if (evt is PaymentRefundedV1 completed) PostgresRefundFinancialPublication.Retain(c, t, result, completed);
        Append(c, t, result, context, now, action, type, evt); t.Commit(); return result;
    }

    internal const string Select = "SELECT id,organization_id,restaurant_id,branch_id,order_id,payment_intent_id,operation_id,amount,currency,reason_code,status,requested_by,authorization_decision_id,requested_at_utc,completed_at_utc,provider_refund_id,failure_code,receipt_snapshot,concurrency_version,lease_owner,lease_expires_at_utc,recovery_attempt_count FROM refunds";
    private static PaymentRefund? Read(NpgsqlConnection c, NpgsqlTransaction? t, Guid org, Guid id, bool update = false)
    { using var command = new NpgsqlCommand(Select + " WHERE organization_id=$1 AND id=$2" + (update ? " FOR UPDATE" : ""), c, t); command.Parameters.AddWithValue(org); command.Parameters.AddWithValue(id); using NpgsqlDataReader r=command.ExecuteReader(); return r.Read()?Map(r):null; }
    private static PaymentRefund? ReadByOperation(NpgsqlConnection c,NpgsqlTransaction t,Guid org,Guid intent,Guid operation)
    { using var command=new NpgsqlCommand(Select+" WHERE organization_id=$1 AND payment_intent_id=$2 AND operation_id=$3",c,t); command.Parameters.AddWithValue(org);command.Parameters.AddWithValue(intent);command.Parameters.AddWithValue(operation);using NpgsqlDataReader r=command.ExecuteReader();return r.Read()?Map(r):null; }
    internal static PaymentRefund Map(NpgsqlDataReader r) => new(r.GetGuid(0),r.GetGuid(1),r.GetGuid(2),r.GetGuid(3),r.GetGuid(4),r.GetGuid(5),r.GetGuid(6),r.GetDecimal(7),r.GetString(8).Trim(),r.GetString(9),r.GetString(10),r.GetString(11),r.GetGuid(12),r.GetFieldValue<DateTimeOffset>(13),r.IsDBNull(14)?null:r.GetFieldValue<DateTimeOffset>(14),r.IsDBNull(15)?null:r.GetString(15),r.IsDBNull(16)?null:r.GetString(16),r.IsDBNull(17)?null:JsonSerializer.Deserialize<PaymentRefundReceipt>(r.GetString(17)),r.GetInt64(18),r.IsDBNull(19)?null:r.GetString(19),r.IsDBNull(20)?null:r.GetFieldValue<DateTimeOffset>(20),r.GetInt32(21));
    private static PaymentIntent? ReadIntent(NpgsqlConnection c,NpgsqlTransaction t,Guid org,Guid id,bool update)
    { using var command=new NpgsqlCommand("SELECT id,organization_id,restaurant_id,branch_id,order_id,amount,currency,payment_method,status,created_at_utc,concurrency_version,provider_authorization_id,failure_code,provider_capture_id FROM payment_intents WHERE organization_id=$1 AND id=$2"+(update?" FOR UPDATE":""),c,t);command.Parameters.AddWithValue(org);command.Parameters.AddWithValue(id);using NpgsqlDataReader r=command.ExecuteReader();return r.Read()?new(r.GetGuid(0),r.GetGuid(1),r.GetGuid(2),r.GetGuid(3),r.GetGuid(4),r.GetDecimal(5),r.GetString(6).Trim(),r.GetString(7),r.GetString(8),r.GetFieldValue<DateTimeOffset>(9),r.GetInt64(10),r.IsDBNull(11)?null:r.GetString(11),r.IsDBNull(12)?null:r.GetString(12),ProviderCaptureId:r.IsDBNull(13)?null:r.GetString(13)):null; }
    private static void Append(NpgsqlConnection c,NpgsqlTransaction t,PaymentRefund refund,PaymentMutationContext context,DateTimeOffset at,string action,string type,IIntegrationEvent evt)
    {
        var auditEvent = new PlatformAuditEventV1(Guid.NewGuid(), context.CorrelationId, at,
            context.ActorSubjectId.Trim(), refund.OrganizationId, action, "payment-refund",
            refund.Id.ToString("D"), "succeeded");
        using (var audit = new NpgsqlCommand("INSERT INTO payment_audit_records(id,organization_id,restaurant_id,branch_id,order_id,payment_intent_id,action,actor_subject_id,occurred_at_utc) VALUES($1,$2,$3,$4,$5,$6,$7,$8,$9)", c, t))
        {
            audit.Parameters.AddWithValue(auditEvent.EventId); audit.Parameters.AddWithValue(refund.OrganizationId);
            audit.Parameters.AddWithValue(refund.RestaurantId); audit.Parameters.AddWithValue(refund.BranchId);
            audit.Parameters.AddWithValue(refund.OrderId); audit.Parameters.AddWithValue(refund.PaymentIntentId);
            audit.Parameters.AddWithValue(action); audit.Parameters.AddWithValue(context.ActorSubjectId.Trim());
            audit.Parameters.AddWithValue(at); audit.ExecuteNonQuery();
        }
        Enqueue(c, t, evt.EventId, type, refund.Id, evt, context.CorrelationId, at);
        Enqueue(c, t, auditEvent.EventId, "payment.audit.v1", refund.Id, auditEvent, context.CorrelationId, at);
    }
    private static void Enqueue(NpgsqlConnection c,NpgsqlTransaction t,Guid id,string type,Guid aggregateId,object payload,Guid correlationId,DateTimeOffset at)
    { using var outbox=new NpgsqlCommand("INSERT INTO outbox_messages(id,event_type,contract_version,aggregate_type,aggregate_id,payload,correlation_id,occurred_at_utc) VALUES($1,$2,1,'payment-refund',$3,$4::jsonb,$5,$6)",c,t);outbox.Parameters.AddWithValue(id);outbox.Parameters.AddWithValue(type);outbox.Parameters.AddWithValue(aggregateId);outbox.Parameters.AddWithValue(JsonSerializer.Serialize(payload));outbox.Parameters.AddWithValue(correlationId.ToString("D"));outbox.Parameters.AddWithValue(at);outbox.ExecuteNonQuery(); }
    private static void Validate(CreatePaymentRefund command,PaymentMutationContext context){ValidateContext(context);if(command.OperationId==Guid.Empty||command.AuthorizationDecisionId==Guid.Empty||command.Amount<=0||decimal.Round(command.Amount,4)!=command.Amount||command.Currency?.Trim().Length!=3||!command.Currency.Trim().All(c=>c is >= 'A' and <= 'Z' or >= 'a' and <= 'z'))throw new ArgumentException("A valid operation, authorization decision, amount and currency are required.");_=InMemoryPaymentRefunds.NormalizeReason(command.ReasonCode);}
    private static void ValidateContext(PaymentMutationContext context){if(context is null||string.IsNullOrWhiteSpace(context.ActorSubjectId)||context.ActorSubjectId.Length>200||context.ActorSubjectId.Any(char.IsControl)||context.CorrelationId==Guid.Empty)throw new ArgumentException("A valid mutation actor and correlation identifier are required.");}
    private static bool SafeReference(string? value)=>!string.IsNullOrWhiteSpace(value)&&value.Trim().Length<=200&&!value.Any(char.IsControl);
}
