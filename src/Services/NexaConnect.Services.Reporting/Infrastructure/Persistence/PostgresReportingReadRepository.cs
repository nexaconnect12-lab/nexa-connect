using NexaConnect.Services.Reporting.Application;
using Npgsql;

namespace NexaConnect.Services.Reporting.Infrastructure.Persistence;

public sealed class PostgresReportingReadRepository(NpgsqlDataSource dataSource) : IReportingReadRepository
{
    public async Task<DashboardSummary> DashboardAsync(ReportingRange range, CancellationToken cancellationToken)
    {
        const string sql = """
            WITH sales AS (
              SELECT count(*) FILTER(WHERE order_status='completed')::int completed,
                COALESCE(sum(total_amount) FILTER(WHERE order_status='completed'),0) gross
              FROM sales_facts WHERE organization_id=$1 AND branch_id=$2 AND ordered_at_utc >= $3 AND ordered_at_utc < $4
            ), payments AS (
              SELECT COALESCE(sum(paid_amount-refunded_amount),0) net_paid FROM payment_facts
              WHERE organization_id=$1 AND branch_id=$2 AND paid_at_utc >= $3 AND paid_at_utc < $4
            ), refunds AS (
              SELECT COALESCE(sum(amount),0) refunded FROM refund_facts
              WHERE organization_id=$1 AND branch_id=$2 AND refunded_at_utc >= $3 AND refunded_at_utc < $4
            ), currencies AS (
              SELECT currency FROM sales_facts WHERE organization_id=$1 AND branch_id=$2 AND ordered_at_utc >= $3 AND ordered_at_utc < $4
              UNION SELECT currency FROM payment_facts WHERE organization_id=$1 AND branch_id=$2 AND paid_at_utc >= $3 AND paid_at_utc < $4
              UNION SELECT currency FROM refund_facts WHERE organization_id=$1 AND branch_id=$2 AND refunded_at_utc >= $3 AND refunded_at_utc < $4
            )
            SELECT sales.completed,sales.gross,payments.net_paid,refunds.refunded,sales.gross-refunds.refunded,
              (SELECT min(currency) FROM currencies),(SELECT count(*) FROM currencies),
              (SELECT max(updated_at_utc) FROM projection_checkpoints)
            FROM sales,payments,refunds;
            """;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = Command(sql, connection, range);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        if (reader.GetInt64(6) > 1) throw new MixedReportingCurrencyException();
        return new(reader.GetInt32(0), reader.GetDecimal(1), reader.GetDecimal(2), reader.GetDecimal(3),
            reader.GetDecimal(4), reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.IsDBNull(7) ? null : reader.GetFieldValue<DateTimeOffset>(7));
    }

    public async Task<SalesReport> SalesAsync(ReportingRange range, CancellationToken cancellationToken)
    {
        const string sql = "SELECT order_id,branch_id,channel,service_type,currency,subtotal_amount,discount_amount,service_charge_amount,tax_amount,total_amount,order_status,ordered_at_utc,completed_at_utc FROM sales_facts WHERE organization_id=$1 AND branch_id=$2 AND ordered_at_utc >= $3 AND ordered_at_utc < $4 ORDER BY ordered_at_utc DESC,order_id LIMIT 1000;";
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = Command(sql, connection, range);
        var items = new List<SalesReportRow>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken)) items.Add(new(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetDecimal(5), reader.GetDecimal(6), reader.GetDecimal(7), reader.GetDecimal(8), reader.GetDecimal(9), reader.GetString(10), reader.GetFieldValue<DateTimeOffset>(11), reader.IsDBNull(12) ? null : reader.GetFieldValue<DateTimeOffset>(12)));
        await using var aggregate = Command("""
            WITH sales AS (
              SELECT COALESCE(sum(total_amount) FILTER(WHERE order_status='completed'),0) gross
              FROM sales_facts WHERE organization_id=$1 AND branch_id=$2 AND ordered_at_utc >= $3 AND ordered_at_utc < $4
            ), refunds AS (
              SELECT COALESCE(sum(amount),0) refunded FROM refund_facts
              WHERE organization_id=$1 AND branch_id=$2 AND refunded_at_utc >= $3 AND refunded_at_utc < $4
            ), currencies AS (
              SELECT currency FROM sales_facts WHERE organization_id=$1 AND branch_id=$2 AND ordered_at_utc >= $3 AND ordered_at_utc < $4
              UNION SELECT currency FROM refund_facts WHERE organization_id=$1 AND branch_id=$2 AND refunded_at_utc >= $3 AND refunded_at_utc < $4
            )
            SELECT sales.gross,refunds.refunded,sales.gross-refunds.refunded,
              (SELECT count(*) FROM currencies),(SELECT min(currency) FROM currencies) FROM sales,refunds;
            """, connection, range);
        await using var aggregateReader = await aggregate.ExecuteReaderAsync(cancellationToken);
        await aggregateReader.ReadAsync(cancellationToken);
        decimal totalSales = aggregateReader.GetDecimal(0);
        decimal refundedAmount = aggregateReader.GetDecimal(1);
        decimal netSales = aggregateReader.GetDecimal(2);
        if (aggregateReader.GetInt64(3) > 1) throw new MixedReportingCurrencyException();
        string? currency = aggregateReader.IsDBNull(4) ? null : aggregateReader.GetString(4);
        await aggregateReader.DisposeAsync();
        await using var freshness = new NpgsqlCommand("SELECT max(updated_at_utc) FROM projection_checkpoints;", connection);
        object? freshValue = await freshness.ExecuteScalarAsync(cancellationToken);
        DateTimeOffset? fresh = freshValue switch
        {
            DateTimeOffset value => value,
            DateTime value => new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)),
            _ => null
        };
        return new(range, items, totalSales, refundedAmount, netSales, currency, fresh);
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
