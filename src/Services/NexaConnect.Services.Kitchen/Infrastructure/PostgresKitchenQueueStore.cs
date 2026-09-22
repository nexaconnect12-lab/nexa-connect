using NexaConnect.Services.Kitchen.Application;
using NexaConnect.Services.Kitchen.Domain;
using Npgsql;
using NpgsqlTypes;

namespace NexaConnect.Services.Kitchen.Infrastructure;

public sealed class PostgresKitchenQueueStore(NpgsqlDataSource dataSource) : IKitchenQueueStore
{
    public async Task<IReadOnlyList<KitchenTicket>> ListActiveAsync(KitchenQueueQuery query, CancellationToken ct)
    {
        const string sql = """
            WITH page AS (
                SELECT * FROM kitchen_tickets
                WHERE organization_id=$1 AND branch_id=$2
                  AND status IN ('queued','in_progress','ready')
                  AND ($3::text IS NULL OR preparation_station_code=$3)
                  AND ($4::timestamptz IS NULL OR (queued_at_utc,id)>($4,$5))
                ORDER BY queued_at_utc,id LIMIT $6
            )
            SELECT p.id,p.organization_id,p.restaurant_id,p.order_id,p.branch_id,p.preparation_station_id,
                   p.status,p.concurrency_version,p.queued_at_utc,p.preparation_station_code,
                   i.product_id,i.item_name_snapshot,i.quantity
            FROM page p JOIN kitchen_ticket_items i ON i.kitchen_ticket_id=p.id
            ORDER BY p.queued_at_utc,p.id,i.queued_at_utc,i.id
            """;
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(query.OrganizationId);
        command.Parameters.AddWithValue(query.BranchId);
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Text, Value = (object?)query.Station ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.TimestampTz, Value = (object?)query.After?.QueuedAtUtc ?? DBNull.Value });
        command.Parameters.AddWithValue(query.After?.TicketId ?? Guid.Empty);
        command.Parameters.AddWithValue(query.Limit + 1);
        var result = new List<KitchenTicket>();
        List<KitchenTicketLine>? lines = null;
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            Guid id = reader.GetGuid(0);
            if (result.Count == 0 || result[^1].TicketId != id)
            {
                lines = [];
                result.Add(new(id, reader.GetGuid(1), reader.GetGuid(2), reader.GetGuid(3), reader.GetGuid(4),
                    reader.GetGuid(5), KitchenTicketLifecycle.Parse(reader.GetString(6)), reader.GetInt64(7),
                    reader.GetFieldValue<DateTimeOffset>(8), lines));
            }
            lines!.Add(new(reader.GetGuid(10), reader.GetString(11), decimal.ToInt32(reader.GetDecimal(12)), reader.GetString(9)));
        }
        return result;
    }
}
