using Npgsql;

namespace NexaConnect.RefundAcceptance.Infrastructure;

internal sealed class FixtureStore(NpgsqlDataSource authorization, NpgsqlDataSource payment, NpgsqlDataSource reporting, NpgsqlDataSource order)
{
    public async Task RuntimePrivilegesAsync(CancellationToken ct)
    {
        await using var version = payment.CreateCommand("SELECT max(version) FROM public.nexaconnect_schema_migrations");
        if (Convert.ToInt32(await version.ExecuteScalarAsync(ct)) != 10) throw new InvalidOperationException("Runtime readiness version unavailable.");
        foreach (string sql in new[] { "SELECT metadata_checksum_sha256 FROM public.nexaconnect_schema_migrations LIMIT 1", "UPDATE public.nexaconnect_schema_migrations SET version=version" })
        {
            await using var connection = await payment.OpenConnectionAsync(ct);
            await using var transaction = await connection.BeginTransactionAsync(ct);
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            bool denied = false;
            try { await command.ExecuteNonQueryAsync(ct); }
            catch (PostgresException e) when (e.SqlState == "42501") { denied = true; }
            finally { await transaction.RollbackAsync(ct); }
            if (!denied) throw new InvalidOperationException("Runtime migration history privilege widened.");
        }
    }
    public async Task GrantBranchCreateAsync(string subject, CancellationToken ct)
    {
        await using var command = authorization.CreateCommand("""
            INSERT INTO authorization_user_permission_overrides(id,subject_id,scope_id,permission_code,effect,status)
            SELECT $1,$2,scope_id,'payment.refund.create','allow','active'
            FROM authorization_role_assignments WHERE subject_id=$2 AND status='active'
            """);
        command.Parameters.AddWithValue(Guid.NewGuid()); command.Parameters.AddWithValue(subject);
        if (await command.ExecuteNonQueryAsync(ct) != 1) throw new InvalidOperationException();
    }
    public async Task LimitAsync(Guid restaurant, string subject, decimal amount, CancellationToken ct)
    {
        await using var command = authorization.CreateCommand("""
            INSERT INTO financial_approval_limits(id,restaurant_id,principal_type,principal_id,action_code,currency,maximum_amount,status)
            VALUES($1,$2,'subject',$3,'payment.refund.create','THB',$4,'active')
            ON CONFLICT(restaurant_id,principal_type,principal_id,action_code,currency)
            DO UPDATE SET maximum_amount=excluded.maximum_amount,status='active'
            """);
        command.Parameters.AddWithValue(Guid.NewGuid()); command.Parameters.AddWithValue(restaurant);
        command.Parameters.AddWithValue(subject); command.Parameters.AddWithValue(amount);
        await command.ExecuteNonQueryAsync(ct);
    }
    public async Task RevokeAsync(string subject, CancellationToken ct)
    {
        await using var command = authorization.CreateCommand("UPDATE authorization_user_permission_overrides SET effect='deny' WHERE subject_id=$1 AND permission_code='payment.refund.create' AND status='active'");
        command.Parameters.AddWithValue(subject);
        if (await command.ExecuteNonQueryAsync(ct) != 1) throw new InvalidOperationException("Expected one fixture override.");
    }
    public async Task AssertEmptyAsync(CancellationToken ct)
    {
        foreach (var pair in new[] { (payment, "SELECT count(*) FROM payment_intents"), (order, "SELECT count(*) FROM orders") })
        {
            await using var command = pair.Item1.CreateCommand(pair.Item2);
            if (Convert.ToInt64(await command.ExecuteScalarAsync(ct)) != 0) throw new InvalidOperationException("Fixture sources are not empty.");
        }
    }
    public async Task ImmutableAsync(Guid refund, CancellationToken ct)
    {
        foreach (string sql in new[] { "UPDATE refunds SET amount=amount+1 WHERE id=$1", "DELETE FROM refunds WHERE id=$1" })
        {
            await using var connection = await payment.OpenConnectionAsync(ct);
            await using var transaction = await connection.BeginTransactionAsync(ct);
            await using var command = new NpgsqlCommand(sql, transaction.Connection, transaction);
            command.Parameters.AddWithValue(refund);
            bool rejected = false;
            try { await command.ExecuteNonQueryAsync(ct); }
            catch (PostgresException e) when (e.SqlState == "P0001") { rejected = true; }
            finally { await transaction.RollbackAsync(ct); }
            if (!rejected) throw new InvalidOperationException("Completed refund mutation was accepted.");
        }
    }
    public async Task VerifyAsync(Guid organization, int completed, decimal total, int sales, CancellationToken ct)
    {
        await using var command = payment.CreateCommand("""
            SELECT count(*),coalesce(sum(amount),0),bool_and(receipt_snapshot IS NOT NULL AND authorization_decision_id IS NOT NULL),
            (SELECT count(*) FROM refund_financial_publications WHERE organization_id=$1)
            FROM refunds WHERE organization_id=$1 AND status='completed'
            """);
        command.Parameters.AddWithValue(organization);
        await using var reader = await command.ExecuteReaderAsync(ct); await reader.ReadAsync(ct);
        if (reader.GetInt64(0) != completed || reader.GetDecimal(1) != total || !reader.GetBoolean(2) || reader.GetInt64(3) != completed)
            throw new InvalidOperationException("Refund source invariants failed.");
        await reader.DisposeAsync();
        await using var paid = order.CreateCommand("SELECT count(*) FROM orders WHERE organization_id=$1 AND status='completed' AND receipt_snapshot IS NOT NULL");
        paid.Parameters.AddWithValue(organization);
        if (Convert.ToInt64(await paid.ExecuteScalarAsync(ct)) != sales) throw new InvalidOperationException("Refund changed paid Order history.");
        await using var audits = payment.CreateCommand("SELECT count(*) FROM payment_audit_records WHERE organization_id=$1 AND action IN('payment.refund.succeeded','payment.refund.reconciled')");
        audits.Parameters.AddWithValue(organization);
        if (Convert.ToInt64(await audits.ExecuteScalarAsync(ct)) != completed) throw new InvalidOperationException("Refund completion audit evidence disagrees.");
        // Scope-qualified facts and receipts must agree after the real consumer commits.
        while (true)
        {
            await using var facts = reporting.CreateCommand("SELECT count(*),coalesce(sum(amount),0),(SELECT count(*) FROM sales_facts WHERE organization_id=$1) FROM refund_facts WHERE organization_id=$1");
            facts.Parameters.AddWithValue(organization);
            await using var rows = await facts.ExecuteReaderAsync(ct); await rows.ReadAsync(ct);
            if (rows.GetInt64(0) == completed && rows.GetDecimal(1) == total && rows.GetInt64(2) == sales) break;
            await Task.Delay(200, ct);
        }
    }
}
