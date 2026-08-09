using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using NexaConnect.Services.Kitchen.Application;
using Npgsql;

namespace NexaConnect.Services.Kitchen.Infrastructure;

public sealed class PostgresKitchenTicketStore(
    NpgsqlDataSource dataSource,
    IOptions<KitchenOptions> options) : IKitchenTicketStore
{
    public async Task<KitchenTicket> CreateAsync(CreateKitchenTicket command, CancellationToken cancellationToken)
    {
        Validate(command);
        Guid ticketId = Guid.NewGuid();
        DateTimeOffset queuedAt = DateTimeOffset.UtcNow;
        Guid stationId = StationId(command.Lines.First().PreparationStation);
        string ticketNumber = $"K-{command.OrderId:N}";

        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        bool created;
        await using (NpgsqlCommand insert = new(
            """
            INSERT INTO kitchen_tickets
                (id, restaurant_id, branch_id, order_id, preparation_station_id, ticket_number,
                 status, queued_at_utc, created_at_utc, updated_at_utc)
            VALUES (@id, @restaurant, @branch, @order, @station, @number,
                    'queued', @queued, @queued, @queued)
            ON CONFLICT (order_id, preparation_station_id, service_sequence)
            DO NOTHING
            RETURNING id
            """, connection, transaction))
        {
            insert.Parameters.AddWithValue("id", ticketId);
            insert.Parameters.AddWithValue("restaurant", options.Value.RestaurantId);
            insert.Parameters.AddWithValue("branch", command.BranchId);
            insert.Parameters.AddWithValue("order", command.OrderId);
            insert.Parameters.AddWithValue("station", stationId);
            insert.Parameters.AddWithValue("number", ticketNumber);
            insert.Parameters.AddWithValue("queued", queuedAt);
            object? inserted = await insert.ExecuteScalarAsync(cancellationToken);
            created = inserted is Guid;
            if (created) ticketId = (Guid)inserted!;
            else
            {
                await using NpgsqlCommand existing = new(
                    "SELECT id FROM kitchen_tickets WHERE order_id=@order AND preparation_station_id=@station AND service_sequence=1",
                    connection, transaction);
                existing.Parameters.AddWithValue("order", command.OrderId);
                existing.Parameters.AddWithValue("station", stationId);
                ticketId = (Guid)(await existing.ExecuteScalarAsync(cancellationToken)
                    ?? throw new InvalidOperationException("Existing Kitchen ticket could not be loaded."));
            }
        }

        if (created)
        {
            foreach ((KitchenTicketLine line, int index) in command.Lines.Select((line, index) => (line, index)))
        {
            await using NpgsqlCommand item = new(
                """
                INSERT INTO kitchen_ticket_items
                    (id, kitchen_ticket_id, order_line_id, product_id, item_name_snapshot,
                     quantity, status, queued_at_utc, updated_at_utc)
                VALUES (@id, @ticket, @line, @product, @name, @quantity, 'queued', @queued, @queued)
                ON CONFLICT (kitchen_ticket_id, order_line_id) DO NOTHING
                """, connection, transaction);
            item.Parameters.AddWithValue("id", Guid.NewGuid());
            item.Parameters.AddWithValue("ticket", ticketId);
            item.Parameters.AddWithValue("line", LineId(command.OrderId, index));
            item.Parameters.AddWithValue("product", line.ProductId);
            item.Parameters.AddWithValue("name", line.Name.Trim());
            item.Parameters.AddWithValue("quantity", line.Quantity);
            item.Parameters.AddWithValue("queued", queuedAt);
            await item.ExecuteNonQueryAsync(cancellationToken);
        }
        }

        await transaction.CommitAsync(cancellationToken);
        return new KitchenTicket(ticketId, command.OrderId, command.BranchId, KitchenTicketStatus.Queued, queuedAt, command.Lines.ToArray());
    }

    public async Task<KitchenTicket?> GetAsync(Guid ticketId, CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = dataSource.CreateCommand(
            "SELECT order_id,branch_id,status,queued_at_utc FROM kitchen_tickets WHERE id=@id");
        command.Parameters.AddWithValue("id", ticketId);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return new KitchenTicket(ticketId, reader.GetGuid(0), reader.GetGuid(1), ParseStatus(reader.GetString(2)),
            reader.GetFieldValue<DateTimeOffset>(3), []);
    }

    public async Task<bool> CancelAsync(Guid orderId, CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = dataSource.CreateCommand(
            "UPDATE kitchen_tickets SET status='cancelled',cancelled_at_utc=now(),updated_at_utc=now(),concurrency_version=concurrency_version+1 WHERE order_id=@order AND status <> 'cancelled'");
        command.Parameters.AddWithValue("order", orderId);
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    private static void Validate(CreateKitchenTicket command)
    {
        if (command.OrderId == Guid.Empty || command.BranchId == Guid.Empty || command.Lines is null || command.Lines.Count == 0)
            throw new ArgumentException("Order, branch, and at least one kitchen line are required.");
        if (command.Lines.Any(line => line.ProductId == Guid.Empty || line.Quantity <= 0 ||
            string.IsNullOrWhiteSpace(line.Name) || string.IsNullOrWhiteSpace(line.PreparationStation)))
            throw new ArgumentException("Kitchen lines require a product, name, positive quantity, and preparation station.");
        if (command.Lines.Select(line => line.PreparationStation.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 1)
            throw new ArgumentException("Kitchen tickets must contain one preparation station; split multi-station orders before calling this service.");
    }

    private static Guid StationId(string station)
    {
        byte[] hash = MD5.HashData(Encoding.UTF8.GetBytes(station.Trim().ToLowerInvariant()));
        return new Guid(hash);
    }

    private static Guid LineId(Guid orderId, int index)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{orderId:D}:{index}"));
        return new Guid(hash[..16]);
    }

    private static KitchenTicketStatus ParseStatus(string status) => status switch
    {
        "queued" => KitchenTicketStatus.Queued,
        "in_progress" => KitchenTicketStatus.InProgress,
        "ready" => KitchenTicketStatus.Ready,
        "completed" => KitchenTicketStatus.Completed,
        "cancelled" => KitchenTicketStatus.Cancelled,
        _ => throw new InvalidOperationException($"Unknown kitchen ticket status '{status}'.")
    };
}
