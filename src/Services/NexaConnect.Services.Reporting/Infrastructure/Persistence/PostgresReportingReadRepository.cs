using NexaConnect.Services.Reporting.Application;
using Npgsql;

namespace NexaConnect.Services.Reporting.Infrastructure.Persistence;

public sealed class PostgresReportingReadRepository(NpgsqlDataSource dataSource) : IReportingReadRepository
{
    public async Task<DashboardSummary> DashboardAsync(ReportingRange range, CancellationToken cancellationToken)
    {
        const string sql = "SELECT count(*) FILTER (WHERE order_status='completed')::int,COALESCE(sum(total_amount) FILTER (WHERE order_status='completed'),0),COALESCE((SELECT sum(paid_amount-refunded_amount) FROM payment_facts p WHERE p.organization_id=$1 AND p.branch_id=$2 AND p.paid_at_utc >= $3 AND p.paid_at_utc < $4),0),COALESCE((SELECT sum(refunded_amount) FROM payment_facts p WHERE p.organization_id=$1 AND p.branch_id=$2 AND p.paid_at_utc >= $3 AND p.paid_at_utc < $4),0),min(currency),(SELECT max(updated_at_utc) FROM projection_checkpoints) FROM sales_facts s WHERE s.organization_id=$1 AND s.branch_id=$2 AND s.ordered_at_utc >= $3 AND s.ordered_at_utc < $4;";
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = Command(sql, connection, range);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return new(reader.GetInt32(0), reader.GetDecimal(1), reader.GetDecimal(2), reader.GetDecimal(3), reader.IsDBNull(4) ? null : reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetFieldValue<DateTimeOffset>(5));
    }

    public async Task<SalesReport> SalesAsync(ReportingRange range, CancellationToken cancellationToken)
    {
        const string sql = "SELECT order_id,branch_id,channel,service_type,currency,subtotal_amount,discount_amount,service_charge_amount,tax_amount,total_amount,order_status,ordered_at_utc,completed_at_utc FROM sales_facts WHERE organization_id=$1 AND branch_id=$2 AND ordered_at_utc >= $3 AND ordered_at_utc < $4 ORDER BY ordered_at_utc DESC,order_id LIMIT 1000;";
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = Command(sql, connection, range);
        var items = new List<SalesReportRow>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken)) items.Add(new(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetDecimal(5), reader.GetDecimal(6), reader.GetDecimal(7), reader.GetDecimal(8), reader.GetDecimal(9), reader.GetString(10), reader.GetFieldValue<DateTimeOffset>(11), reader.IsDBNull(12) ? null : reader.GetFieldValue<DateTimeOffset>(12)));
        await using var freshness = new NpgsqlCommand("SELECT max(updated_at_utc) FROM projection_checkpoints;", connection);
        object? freshValue = await freshness.ExecuteScalarAsync(cancellationToken);
        DateTimeOffset? fresh = freshValue is DateTimeOffset value ? value : null;
        string? currency=items.Select(item=>item.Currency).Distinct(StringComparer.Ordinal).SingleOrDefault();
        return new(range, items, items.Where(item => item.OrderStatus == "completed").Sum(item => item.TotalAmount), currency, fresh);
    }

    private static NpgsqlCommand Command(string sql, NpgsqlConnection connection, ReportingRange range)
    {
        var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(range.OrganizationId);
        command.Parameters.AddWithValue(range.BranchId);
        command.Parameters.AddWithValue(range.FromUtc);
        command.Parameters.AddWithValue(range.ToUtc);
        return command;
    }
}
