using Microsoft.AspNetCore.DataProtection;
using System.Text.Json;
using NexaConnect.Contracts.IntegrationEvents;
using NexaConnect.Observability;
using NexaConnect.Contracts.POS;
using NexaConnect.Services.POS.Application.ManualRefunds;
using NexaConnect.Services.POS.Application.Shifts;
using NexaConnect.Services.POS.Domain.ManualRefunds;
using Npgsql;

namespace NexaConnect.Services.POS.Infrastructure.Persistence;

public sealed class PostgresManualRefundStore(NpgsqlDataSource source, IDataProtectionProvider protection) : IManualRefundStore
{
    private const int MaximumApprovals = 1000;

    public async Task<bool> RegisterCashierAsync(RestaurantAuthorizationScope scope, ManualRefundAvailabilityV1 terminal,
        string subject, DateTimeOffset now, CancellationToken ct)
    {
        await using var query = source.CreateCommand("""
            INSERT INTO manual_refund_cashier_readiness (shift_id, organization_id, cashier_subject, checked_at_utc, expires_at_utc)
            SELECT s.id, $1, $2, $3, $4
            FROM shifts s JOIN terminals t ON t.id=s.terminal_id AND t.store_id=s.store_id
            JOIN stores b ON b.id=s.store_id AND b.restaurant_id=t.restaurant_id
            WHERE s.id=$5 AND t.id=$6 AND s.employee_identity_subject_id=$2
              AND b.restaurant_id=$7 AND b.branch_id=$8 AND s.status='open' AND s.closed_at_utc IS NULL
              AND s.opened_at_utc<=$3 AND t.registration_status='active' AND b.operational_status='active'
            ON CONFLICT (shift_id) DO UPDATE SET checked_at_utc=excluded.checked_at_utc, expires_at_utc=excluded.expires_at_utc
            WHERE manual_refund_cashier_readiness.organization_id=excluded.organization_id
              AND manual_refund_cashier_readiness.cashier_subject=excluded.cashier_subject
            """);
        foreach (var value in new object[] { scope.OrganizationId, subject, now, now.AddMinutes(5), terminal.ShiftId,
            terminal.TerminalId, scope.RestaurantId, scope.BranchId })
            query.Parameters.AddWithValue(value);
        return await query.ExecuteNonQueryAsync(ct) == 1;
    }

    public async Task<IReadOnlyList<ManualRefundCashierV1>> ListReadyCashiersAsync(RestaurantAuthorizationScope scope,
        DateTimeOffset now, CancellationToken ct)
    {
        await using var query = source.CreateCommand("""
            SELECT s.id, r.cashier_subject, t.code, s.shift_number,
                EXISTS (SELECT 1 FROM cash_sessions c WHERE c.shift_id=s.id AND c.store_id=s.store_id
                    AND c.status='open' AND c.closed_at_utc IS NULL AND c.currency='THB')
            FROM manual_refund_cashier_readiness r JOIN shifts s ON s.id=r.shift_id
            JOIN terminals t ON t.id=s.terminal_id AND t.store_id=s.store_id
            JOIN stores b ON b.id=s.store_id AND b.restaurant_id=t.restaurant_id
            WHERE r.organization_id=$1 AND b.restaurant_id=$2 AND b.branch_id=$3 AND r.expires_at_utc>$4
              AND s.status='open' AND s.closed_at_utc IS NULL AND s.employee_identity_subject_id=r.cashier_subject
              AND t.registration_status='active' AND b.operational_status='active'
            ORDER BY r.checked_at_utc DESC, s.id LIMIT 50
            """);
        foreach (var value in new object[] { scope.OrganizationId, scope.RestaurantId, scope.BranchId, now })
            query.Parameters.AddWithValue(value);
        await using var reader = await query.ExecuteReaderAsync(ct);
        var result = new List<ManualRefundCashierV1>();
        while (await reader.ReadAsync(ct))
            result.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetBoolean(4)));
        return result;
    }

    private static async Task RequireReadyCashierAsync(OriginalManualSale sale, Guid shiftId, ManualRefundApproval approval,
        NpgsqlTransaction transaction, CancellationToken ct)
    {
        await using var check = Command("""
            SELECT s.store_id
            FROM manual_refund_cashier_readiness r JOIN shifts s ON s.id=r.shift_id
            JOIN terminals t ON t.id=s.terminal_id AND t.store_id=s.store_id
            JOIN stores b ON b.id=s.store_id AND b.restaurant_id=t.restaurant_id
            WHERE r.shift_id=$1 AND r.organization_id=$2 AND b.restaurant_id=$3 AND b.branch_id=$4
              AND r.cashier_subject=$5 AND s.employee_identity_subject_id=$5
              AND r.expires_at_utc>GREATEST($6, clock_timestamp())
              AND s.status='open' AND s.closed_at_utc IS NULL
              AND t.registration_status='active' AND b.operational_status='active'
            FOR SHARE OF r,s,t,b
            """, transaction, shiftId, sale.OrganizationId, sale.RestaurantId, sale.BranchId, approval.CashierSubject, approval.ApprovedAtUtc);
        if (await check.ExecuteScalarAsync(ct) is not Guid storeId)
            throw new InvalidOperationException("The selected cashier is no longer available.");
        if (sale.Tender != ManualRefundTender.Cash) return;
        await using var drawer = Command("""
            SELECT id FROM cash_sessions WHERE shift_id=$1 AND store_id=$2 AND currency='THB'
              AND status='open' AND closed_at_utc IS NULL FOR SHARE
            """, transaction, shiftId, storeId);
        if (await drawer.ExecuteScalarAsync(ct) is null)
            throw new InvalidOperationException("The selected cashier's drawer is no longer open.");
    }

    private const string ReadOrderRefunds = """
            SELECT s.settlement_id, s.tender, s.paid_amount, btrim(s.currency), s.paid_at_utc,
                   a.amount, a.reason, a.manager_subject, a.authorization_decision_id, a.cashier_subject,
                   a.approved_at_utc, p.started_at_utc, c.recorded_at_utc, r.released_at_utc, a.operation_id
            FROM manual_refund_sales s
            JOIN manual_refund_approvals a ON a.order_id=s.order_id
            LEFT JOIN manual_refund_payouts p ON p.operation_id=a.operation_id
            LEFT JOIN manual_refund_completions c ON c.operation_id=a.operation_id
            LEFT JOIN manual_refund_releases r ON r.operation_id=a.operation_id
            WHERE s.organization_id=$1 AND s.restaurant_id=$2 AND s.branch_id=$3 AND s.order_id=$4
            """;

    public async Task<StoredManualRefund?> ReadAsync(RestaurantAuthorizationScope scope, Guid orderId, Guid operationId, CancellationToken ct)
    {
        await using var query = source.CreateCommand(ReadOrderRefunds + " AND a.operation_id=$5");
        foreach (Guid id in new[] { scope.OrganizationId, scope.RestaurantId, scope.BranchId, orderId, operationId })
            query.Parameters.AddWithValue(id);
        await using var reader = await query.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadStoredRefund(reader, scope, orderId) : null;
    }

    public async Task<IReadOnlyList<StoredManualRefund>> ListManagedAsync(RestaurantAuthorizationScope scope, Guid orderId,
        string manager, int limit, CancellationToken ct)
    {
        await using var query = source.CreateCommand(ReadOrderRefunds
            + " AND a.manager_subject=$5 ORDER BY (p.operation_id IS NOT NULL OR r.operation_id IS NOT NULL), a.approved_at_utc DESC, a.operation_id LIMIT $6");
        foreach (var value in new object[] { scope.OrganizationId, scope.RestaurantId, scope.BranchId, orderId, manager, limit })
            query.Parameters.AddWithValue(value);
        await using var reader = await query.ExecuteReaderAsync(ct);
        var results = new List<StoredManualRefund>();
        while (await reader.ReadAsync(ct)) results.Add(ReadStoredRefund(reader, scope, orderId));
        return results;
    }

    private static StoredManualRefund ReadStoredRefund(NpgsqlDataReader reader, RestaurantAuthorizationScope scope, Guid orderId)
    {
        var tender = reader.GetString(1) == "cash" ? ManualRefundTender.Cash : ManualRefundTender.PromptPay;
        var sale = new OriginalManualSale(scope.OrganizationId, scope.RestaurantId, scope.BranchId, orderId, reader.GetGuid(0),
            tender, reader.GetDecimal(2), reader.GetString(3), reader.GetFieldValue<DateTimeOffset>(4));
        var approval = new ManualRefundApproval(reader.GetGuid(14), reader.GetDecimal(5), reader.GetString(6), reader.GetString(7),
            reader.GetGuid(8), reader.GetString(9), reader.GetFieldValue<DateTimeOffset>(10));
        return new(sale, approval, reader.IsDBNull(11) ? null : reader.GetFieldValue<DateTimeOffset>(11),
            reader.IsDBNull(12) ? null : reader.GetFieldValue<DateTimeOffset>(12),
            reader.IsDBNull(13) ? null : reader.GetFieldValue<DateTimeOffset>(13));
    }

    public async Task<decimal> AvailableAsync(OriginalManualSale sale, CancellationToken ct)
    {
        await using var query = source.CreateCommand("""
            SELECT COALESCE(SUM(a.amount), 0)
            FROM manual_refund_sales s JOIN manual_refund_approvals a ON a.order_id=s.order_id
            WHERE s.order_id=$1 AND s.organization_id=$2 AND s.restaurant_id=$3 AND s.branch_id=$4 AND s.settlement_id=$5
                AND NOT EXISTS (SELECT 1 FROM manual_refund_releases r WHERE r.operation_id=a.operation_id)
            """);
        foreach (Guid id in new[] { sale.OrderId, sale.OrganizationId, sale.RestaurantId, sale.BranchId, sale.SettlementId })
            query.Parameters.AddWithValue(id);
        var reserved = (decimal)(await query.ExecuteScalarAsync(ct))!;
        return reserved <= sale.PaidAmount ? sale.PaidAmount - reserved : throw new InvalidOperationException("Refund balance is inconsistent.");
    }

    public async Task<IReadOnlyList<StoredManualRefund>> ListOutstandingAsync(RestaurantAuthorizationScope scope,
        string cashierSubject, int limit, CancellationToken ct)
    {
        await using var query = source.CreateCommand("""
            SELECT s.order_id, s.settlement_id, s.tender, s.paid_amount, btrim(s.currency), s.paid_at_utc,
                   a.operation_id, a.amount, a.reason, a.manager_subject, a.authorization_decision_id,
                   a.approved_at_utc, p.started_at_utc
            FROM manual_refund_approvals a
            JOIN manual_refund_sales s ON s.order_id=a.order_id
            LEFT JOIN manual_refund_payouts p ON p.operation_id=a.operation_id
            LEFT JOIN manual_refund_completions c ON c.operation_id=a.operation_id
            WHERE s.organization_id=$1 AND s.restaurant_id=$2 AND s.branch_id=$3
              AND a.cashier_subject=$4 AND c.operation_id IS NULL
              AND NOT EXISTS (SELECT 1 FROM manual_refund_releases r WHERE r.operation_id=a.operation_id)
            ORDER BY (p.operation_id IS NULL), a.approved_at_utc, a.operation_id
            LIMIT $5
            """);
        query.Parameters.AddWithValue(scope.OrganizationId);
        query.Parameters.AddWithValue(scope.RestaurantId);
        query.Parameters.AddWithValue(scope.BranchId);
        query.Parameters.AddWithValue(cashierSubject);
        query.Parameters.AddWithValue(limit);
        await using var reader = await query.ExecuteReaderAsync(ct);
        var results = new List<StoredManualRefund>();
        while (await reader.ReadAsync(ct))
        {
            var tender = reader.GetString(2) == "cash" ? ManualRefundTender.Cash : ManualRefundTender.PromptPay;
            var sale = new OriginalManualSale(scope.OrganizationId, scope.RestaurantId, scope.BranchId, reader.GetGuid(0),
                reader.GetGuid(1), tender, reader.GetDecimal(3), reader.GetString(4), reader.GetFieldValue<DateTimeOffset>(5));
            var approval = new ManualRefundApproval(reader.GetGuid(6), reader.GetDecimal(7), reader.GetString(8),
                reader.GetString(9), reader.GetGuid(10), cashierSubject, reader.GetFieldValue<DateTimeOffset>(11));
            results.Add(new(sale, approval, reader.IsDBNull(12) ? null : reader.GetFieldValue<DateTimeOffset>(12), null));
        }
        return results;
    }

    public Task<ManualRefundWrite> ApproveAsync(OriginalManualSale sale, ManualRefundApproval approval, CancellationToken ct, Guid? cashierShiftId = null) =>
        WriteAsync(sale, approval.OperationId, ledger => ledger.Approve(approval), createSale: true, ct, cashierShiftId);

    public Task<ManualRefundWrite> ReleaseAsync(OriginalManualSale sale, Guid operationId, ManualRefundRelease release, CancellationToken ct) =>
        WriteAsync(sale, operationId, ledger => ledger.Release(operationId, release), createSale: false, ct);

    public Task<ManualRefundWrite> BeginAsync(OriginalManualSale sale, Guid operationId, ManualRefundPayout payout, CancellationToken ct) =>
        WriteAsync(sale, operationId, ledger => ledger.BeginPayout(operationId, payout), createSale: false, ct);

    public Task<ManualRefundWrite> CompleteAsync(OriginalManualSale sale, Guid operationId, ManualRefundPayout custody,
        bool transferVerified, string? transferReference, DateTimeOffset now, CancellationToken ct) =>
        WriteAsync(sale, operationId, ledger => ledger.Complete(operationId, custody, transferVerified, transferReference, now),
            createSale: false, ct);

    private async Task<ManualRefundWrite> WriteAsync(OriginalManualSale sale, Guid operationId,
        Func<ManualRefundLedger, ManualRefund> transition, bool createSale, CancellationToken ct, Guid? cashierShiftId = null)
    {
        var paidAt = new DateTimeOffset(sale.PaidAtUtc.UtcTicks / 10 * 10, TimeSpan.Zero);
        var ledger = new ManualRefundLedger(sale.OrganizationId, sale.RestaurantId, sale.BranchId, sale.OrderId,
            sale.SettlementId, sale.Tender, sale.PaidAmount, sale.Currency, paidAt);
        string tender = sale.Tender == ManualRefundTender.Cash ? "cash" : "promptpay_manual";
        await using var connection = await source.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        if (createSale)
        {
            await ExecuteAsync("""
                INSERT INTO manual_refund_sales
                    (order_id, settlement_id, organization_id, restaurant_id, branch_id, tender, currency, paid_amount, paid_at_utc)
                VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9) ON CONFLICT (order_id) DO NOTHING
                """, transaction, ct, sale.OrderId, sale.SettlementId, sale.OrganizationId, sale.RestaurantId, sale.BranchId,
                tender, sale.Currency, sale.PaidAmount, paidAt);
        }

        await using (var saleLock = Command("""
            SELECT order_id FROM manual_refund_sales
            WHERE order_id=$1 AND settlement_id=$2 AND organization_id=$3 AND restaurant_id=$4 AND branch_id=$5
                AND tender=$6 AND currency=$7 AND paid_amount=$8 AND paid_at_utc=$9
            FOR UPDATE
            """, transaction, sale.OrderId, sale.SettlementId, sale.OrganizationId, sale.RestaurantId, sale.BranchId,
            tender, sale.Currency, sale.PaidAmount, paidAt))
        {
            if (await saleLock.ExecuteScalarAsync(ct) is null)
                throw new InvalidOperationException("Original manual settlement evidence is unavailable or changed.");
        }

        int count = await RestoreAsync(ledger, transaction, ct);
        var previous = ledger.Read(operationId);
        var updated = transition(ledger);
        if (ReferenceEquals(previous, updated))
        {
            await transaction.CommitAsync(ct);
            return new(updated, true);
        }

        if (previous is null)
        {
            if (count >= MaximumApprovals)
                throw new InvalidOperationException("Manual refund history reached its supported limit.");
            var approval = updated.Approval;
            if (cashierShiftId is { } shift)
                await RequireReadyCashierAsync(sale, shift, approval, transaction, ct);
            await ExecuteAsync("""
                INSERT INTO manual_refund_approvals
                    (operation_id, order_id, amount, reason, manager_subject, authorization_decision_id, cashier_subject, approved_at_utc)
                VALUES ($1,$2,$3,$4,$5,$6,$7,$8)
                """, transaction, ct, operationId, sale.OrderId, approval.Amount, approval.Reason, approval.ManagerSubject,
                approval.AuthorizationDecisionId, approval.CashierSubject, approval.ApprovedAtUtc);
        }
        else if (updated.Release is { } release)
        {
            await ExecuteAsync("""
                INSERT INTO manual_refund_releases (operation_id, manager_subject, authorization_decision_id, released_at_utc)
                VALUES ($1,$2,$3,$4)
                """, transaction, ct, operationId, release.ManagerSubject, release.AuthorizationDecisionId, release.ReleasedAtUtc);
        }
        else if (previous.Payout is null)
        {
            var payout = updated.Payout!;
            await LockCustodyAsync(sale, payout, payout.StartedAtUtc, transaction, ct);
            await ExecuteAsync("""
                INSERT INTO manual_refund_payouts (operation_id, terminal_id, shift_id, cash_session_id, started_at_utc)
                VALUES ($1,$2,$3,NULLIF($4, '00000000-0000-0000-0000-000000000000'::uuid),$5)
                """, transaction, ct, operationId, payout.TerminalId, payout.ShiftId, payout.CashSessionId ?? Guid.Empty, payout.StartedAtUtc);
        }
        else
        {
            await RecordCompletionAsync(sale, updated, transaction, ct);
        }

        await transaction.CommitAsync(ct);
        return new(updated, false);
    }

    private async Task<int> RestoreAsync(ManualRefundLedger ledger, NpgsqlTransaction transaction, CancellationToken ct)
    {
        await using var command = Command("""
            SELECT a.operation_id, a.amount, a.reason, a.manager_subject, a.authorization_decision_id, a.cashier_subject, a.approved_at_utc,
                   p.terminal_id, p.shift_id, p.cash_session_id, p.started_at_utc, c.recorded_at_utc, c.protected_transfer_reference,
                   r.manager_subject, r.authorization_decision_id, r.released_at_utc
            FROM manual_refund_approvals a
            LEFT JOIN manual_refund_payouts p USING (operation_id)
            LEFT JOIN manual_refund_completions c USING (operation_id)
            LEFT JOIN manual_refund_releases r USING (operation_id)
            WHERE a.order_id=$1 ORDER BY (r.operation_id IS NULL), a.operation_id LIMIT $2
            """, transaction, ledger.OrderId, MaximumApprovals + 1);
        await using var reader = await command.ExecuteReaderAsync(ct);
        int count = 0;
        while (await reader.ReadAsync(ct))
        {
            if (++count > MaximumApprovals) throw new InvalidOperationException("Manual refund history exceeds its supported limit.");
            var approval = new ManualRefundApproval(reader.GetGuid(0), reader.GetDecimal(1), reader.GetString(2),
                reader.GetString(3), reader.GetGuid(4), reader.GetString(5), reader.GetFieldValue<DateTimeOffset>(6));
            ledger.Approve(approval);
            if (!reader.IsDBNull(13))
            {
                if (!reader.IsDBNull(7)) throw new InvalidOperationException("A released refund cannot have payout evidence.");
                ledger.Release(approval.OperationId, new(reader.GetString(13), reader.GetGuid(14), reader.GetFieldValue<DateTimeOffset>(15)));
                continue;
            }
            if (reader.IsDBNull(7)) continue;
            var payout = new ManualRefundPayout(reader.GetGuid(7), reader.GetGuid(8),
                reader.IsDBNull(9) ? null : reader.GetGuid(9), approval.CashierSubject, reader.GetFieldValue<DateTimeOffset>(10));
            ledger.BeginPayout(approval.OperationId, payout);
            if (reader.IsDBNull(11)) continue;
            string? reference = reader.IsDBNull(12) ? null : Protector(approval.OperationId).Unprotect(reader.GetString(12));
            ledger.Complete(approval.OperationId, payout, reference is not null, reference, reader.GetFieldValue<DateTimeOffset>(11));
        }
        return count;
    }

    private async Task RecordCompletionAsync(OriginalManualSale sale, ManualRefund refund, NpgsqlTransaction transaction, CancellationToken ct)
    {
        var payout = refund.Payout!;
        var completion = refund.Completion!;
        await LockCustodyAsync(sale, payout, completion.RecordedAtUtc, transaction, ct);
        Guid movementId = Guid.Empty;
        if (payout.CashSessionId is { } cashSession)
        {
            movementId = Guid.NewGuid();
            await ExecuteAsync("""
                INSERT INTO cash_movements (id, cash_session_id, movement_type, amount, order_id, reason_code, occurred_at_utc, recorded_by)
                VALUES ($1,$2,'refund',$3,$4,$5,$6,$7)
                """, transaction, ct, movementId, cashSession, refund.Approval.Amount, sale.OrderId, refund.Approval.Reason,
                completion.RecordedAtUtc, payout.CashierSubject);
            await ExecuteAsync("""
                UPDATE cash_sessions SET concurrency_version=concurrency_version+1, updated_at_utc=GREATEST(updated_at_utc,$2)
                WHERE id=$1
                """, transaction, ct, cashSession, completion.RecordedAtUtc);
        }

        string encrypted = completion.TransferReference is { } reference
            ? Protector(refund.Approval.OperationId).Protect(reference) : "";
        await ExecuteAsync("""
            INSERT INTO manual_refund_completions (operation_id, cash_movement_id, protected_transfer_reference, recorded_at_utc)
            VALUES ($1,NULLIF($2, '00000000-0000-0000-0000-000000000000'::uuid),NULLIF($3,''),$4)
            """, transaction, ct, refund.Approval.OperationId, movementId, encrypted, completion.RecordedAtUtc);
        var correlation = Guid.TryParse(CorrelationContext.Current, out var supplied) && supplied != Guid.Empty
            ? supplied : Guid.NewGuid();
        var completedAt = new DateTimeOffset(completion.RecordedAtUtc.UtcTicks / 10 * 10, TimeSpan.Zero);
        var message = new PosManualRefundCompletedV1(Guid.NewGuid(), correlation, completedAt,
            sale.OrganizationId, sale.RestaurantId, sale.BranchId, sale.OrderId, sale.SettlementId,
            refund.Approval.OperationId, sale.Tender == ManualRefundTender.Cash ? "cash" : "promptpay_manual",
            refund.Approval.Amount, sale.Currency, movementId == Guid.Empty ? null : movementId);
        await ExecuteAsync("""
            INSERT INTO outbox_messages
                (id,event_type,contract_version,aggregate_type,aggregate_id,payload,correlation_id,occurred_at_utc)
            VALUES ($1,$2,1,'manual-refund',$3,$4::jsonb,$5,$6)
            """, transaction, ct, message.EventId, PosManualRefundCompletedV1.EventType, message.OperationId,
            JsonSerializer.Serialize(message), correlation.ToString("D"), completedAt);
    }

    private static async Task LockCustodyAsync(OriginalManualSale sale, ManualRefundPayout payout, DateTimeOffset at,
        NpgsqlTransaction transaction, CancellationToken ct)
    {
        await using var shift = Command("""
            SELECT shift.store_id FROM shifts shift
            JOIN terminals terminal ON terminal.id=shift.terminal_id AND terminal.store_id=shift.store_id
            JOIN stores store ON store.id=shift.store_id
            WHERE shift.id=$1 AND terminal.id=$2 AND shift.employee_identity_subject_id=$3
                AND store.restaurant_id=$4 AND store.branch_id=$5 AND terminal.restaurant_id=$4
                AND shift.status='open' AND shift.closed_at_utc IS NULL AND shift.opened_at_utc <= $6
                AND terminal.registration_status='active' AND store.operational_status='active'
            FOR UPDATE OF shift
            FOR SHARE OF terminal, store
            """, transaction, payout.ShiftId, payout.TerminalId, payout.CashierSubject, sale.RestaurantId, sale.BranchId, at);
        if (await shift.ExecuteScalarAsync(ct) is not Guid storeId)
            throw new InvalidOperationException("The approved cashier's terminal shift is unavailable.");
        if (payout.CashSessionId is not { } sessionId) return;
        await using var drawer = Command("""
            SELECT id FROM cash_sessions
            WHERE id=$1 AND shift_id=$2 AND store_id=$3 AND currency=$4 AND status='open'
                AND closed_at_utc IS NULL AND opened_at_utc <= $5
            FOR UPDATE
            """, transaction, sessionId, payout.ShiftId, storeId, sale.Currency, at);
        if (await drawer.ExecuteScalarAsync(ct) is null)
            throw new InvalidOperationException("The original paying drawer is unavailable.");
    }

    private IDataProtector Protector(Guid operationId) => protection.CreateProtector("pos.manual-refund.transfer.v1", operationId.ToString("D"));

    private static NpgsqlCommand Command(string sql, NpgsqlTransaction transaction, params object[] values)
    {
        var command = new NpgsqlCommand(sql, transaction.Connection, transaction);
        foreach (object value in values)
            command.Parameters.AddWithValue(value is DateTimeOffset instant ? instant.ToUniversalTime() : value);
        return command;
    }

    private static async Task ExecuteAsync(string sql, NpgsqlTransaction transaction, CancellationToken ct, params object[] values)
    {
        await using var command = Command(sql, transaction, values);
        await command.ExecuteNonQueryAsync(ct);
    }
}
