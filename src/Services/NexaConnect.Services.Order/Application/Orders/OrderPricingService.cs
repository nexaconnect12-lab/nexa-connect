using System.Security.Cryptography;
using System.Text.Json;
using NexaConnect.Services.Order.Application.Workflow;
using NexaConnect.Services.Order.Domain;

namespace NexaConnect.Services.Order.Application.Orders;

public interface IBranchPricingPort
{
    Task<PricingPolicy> GetAsync(Guid organizationId, Guid restaurantId, Guid branchId, CancellationToken cancellationToken);
}

public sealed record OrderQuote(string Fingerprint, OrderPricing Pricing, IReadOnlyList<OrderLine> Lines);
public sealed class PricingChangedException : Exception;
public sealed class OrderPlacementConflictException : Exception;

public sealed class OrderPricingService(IMenuCatalogPort catalog, IBranchPricingPort policies)
{
    public async Task<OrderQuote> QuoteAsync(PlaceOrderCommand command, CancellationToken cancellationToken)
    {
        if (command.OrganizationId == Guid.Empty || command.BranchId == Guid.Empty
            || command.RestaurantId is null || command.RestaurantId == Guid.Empty
            || command.Currency != "THB" || command.Lines is null || command.Lines.Count is 0 or > 200
            || command.Lines.Any(l => l.ProductId == Guid.Empty || l.Quantity is <= 0 or > 10000)
            || command.Lines.Select(l => l.ProductId).Distinct().Count() != command.Lines.Count)
            throw new ArgumentException("Invalid checkout scope or lines.");
        PricingPolicy policy = await policies.GetAsync(command.OrganizationId, command.RestaurantId.Value, command.BranchId, cancellationToken);
        var items = await catalog.GetItemsAsync(command.BranchId, command.Lines.Select(l => l.ProductId).ToArray(), cancellationToken);
        var lines = command.Lines.OrderBy(l => l.ProductId).Select(l =>
        {
            if (!items.TryGetValue(l.ProductId, out var item) || !item.Available || item.Currency != command.Currency)
                throw new ArgumentException("An item is unavailable or has a different currency.");
            return new OrderLine(item.ProductId, item.Name, item.UnitPrice, l.Quantity, item.PreparationStation);
        }).ToArray();
        var pricing = OrderPricing.Calculate(lines, command.Currency, policy);
        // Hash server-resolved commercial data and request scope; this is a comparison token, not authorization.
        string fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            command.OrganizationId, command.RestaurantId, command.BranchId, command.Currency,
            command.PaymentMethod, pricing, lines
        })));
        return new(fingerprint, pricing, lines);
    }
}
