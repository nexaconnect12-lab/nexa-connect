using System.Globalization;
using System.Text;
using NexaConnect.Contracts.Platform;
using NexaConnect.Services.Order.Application.Tenant;
using NexaConnect.Services.Order.Domain;
using NexaConnect.Services.Order.Domain.Qr;

namespace NexaConnect.Services.Order.Application.Orders;

public sealed record OrderListScope(Guid OrganizationId, Guid RestaurantId, Guid BranchId);
public sealed record OrderListCursor(DateTimeOffset CreatedAtUtc, Guid OrderId)
{
    public string Encode() => Convert.ToBase64String(Encoding.UTF8.GetBytes(
        CreatedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) + "|" + OrderId.ToString("D")))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static OrderListCursor? Parse(string? value)
    {
        if (value is null) return null;
        if (value.Length is < 1 or > 128 || value.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_')))
            throw new ArgumentException("Invalid order page cursor.");
        try
        {
            string encoded = value.Replace('-', '+').Replace('_', '/');
            string[] parts = Encoding.UTF8.GetString(Convert.FromBase64String(encoded.PadRight((encoded.Length + 3) / 4 * 4, '='))).Split('|');
            if (parts.Length != 2 || !DateTimeOffset.TryParseExact(parts[0], "O", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var time) || !Guid.TryParseExact(parts[1], "D", out var id) || id == Guid.Empty)
                throw new ArgumentException("Invalid order page cursor.");
            var cursor = new OrderListCursor(time.ToUniversalTime(), id);
            if (cursor.Encode() != value) throw new ArgumentException("Invalid order page cursor.");
            return cursor;
        }
        catch (FormatException) { throw new ArgumentException("Invalid order page cursor."); }
    }
}
public sealed record OrderListRow(Guid OrderId, OrderListScope Scope, string OrderNumber,
    DateTimeOffset CreatedAtUtc, string Channel, string Status, string Currency, decimal TotalAmount,
    string? TableCode, bool AcceptedQr, bool HasPricing, bool HasProviderIntent, bool HasCollection);
public sealed record BranchOrderItem(Guid OrderId, string OrderNumber, DateTimeOffset CreatedAtUtc,
    string Channel, string Status, string Currency, decimal TotalAmount, string? TableCode,
    string? CollectionReference, string? CollectionStatus, bool CanCollectQr);
public sealed record BranchOrderPage(Guid OrganizationId, Guid RestaurantId, Guid BranchId,
    IReadOnlyList<BranchOrderItem> Items, string? NextCursor);

public interface IOrderListRepository
{
    Task<IReadOnlyList<OrderListRow>> ReadAsync(OrderListScope scope, string channel, string state,
        OrderListCursor? cursor, int fetchLimit, CancellationToken ct);
}

public sealed class OrderListService(IOrderListRepository repository, IOrderTenantAuthorizer authorization,
    bool qrCollectionEnabled)
{
    public async Task<BranchOrderPage> ListAsync(OrderListScope scope, string channel, string state,
        string? cursor, int limit, string bearer, CancellationToken ct)
    {
        if (scope.OrganizationId == Guid.Empty || scope.RestaurantId == Guid.Empty || scope.BranchId == Guid.Empty
            || channel is not ("all" or "qr" or "pos") || state is not ("all" or "open" or "paid") || limit is < 1 or > 100)
            throw new ArgumentException("Invalid order list scope or filter.");
        var after = OrderListCursor.Parse(cursor);
        var read = await authorization.GetBranchDecisionAsync(scope.OrganizationId, scope.BranchId,
            ProductPermissions.OrderRead, bearer, ct);
        if (read is null || read == Guid.Empty) throw new UnauthorizedAccessException();
        bool canCollect = false;
        if (qrCollectionEnabled)
        {
            var payment = await authorization.GetBranchDecisionAsync(scope.OrganizationId, scope.BranchId,
                ProductPermissions.OrderManualPaymentConfirm, bearer, ct);
            canCollect = payment is not null && payment != Guid.Empty;
        }
        var rows = await repository.ReadAsync(scope, channel, state, after, limit + 1, ct);
        if (rows.Count > limit + 1 || rows.Any(r => r.Scope != scope || r.OrderId == Guid.Empty)
            || rows.Select(r => r.OrderId).Distinct().Count() != rows.Count)
            throw new InvalidDataException("Order list projection scope is invalid.");
        var items = rows.Take(limit).Select(row =>
        {
            bool readableQr = row.AcceptedQr && row.Channel == "qr";
            bool knownStatus = Enum.TryParse<OrderStatus>(row.Status, out var status) && Enum.IsDefined(status);
            bool eligible = readableQr && knownStatus && status != OrderStatus.Paid && !row.HasCollection
                && QrCollectionBinding.CanReadOriginalOrder(row.Channel, status, row.HasPricing, row.HasProviderIntent);
            string? collection = !readableQr ? null : row.Status == "Paid" ? "paid"
                : row.Status is not ("Submitted" or "InventoryReserved" or "KitchenAccepted") ? "unavailable"
                : row.HasCollection ? "collecting" : eligible ? "awaiting_cashier"
                : row.Status is "Submitted" or "InventoryReserved" ? "preparing" : "unavailable";
            return new BranchOrderItem(row.OrderId, row.OrderNumber, row.CreatedAtUtc, row.Channel, row.Status,
                row.Currency, row.TotalAmount, row.TableCode, readableQr ? "QR-" + row.OrderId.ToString("N") : null,
                collection, canCollect && eligible);
        }).ToArray();
        string? next = rows.Count > limit ? new OrderListCursor(rows[limit - 1].CreatedAtUtc, rows[limit - 1].OrderId).Encode() : null;
        return new(scope.OrganizationId, scope.RestaurantId, scope.BranchId, items, next);
    }
}
