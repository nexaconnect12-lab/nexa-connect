using NexaConnect.Contracts.RestaurantQr;
using NexaConnect.Services.Order.Application.Workflow;

namespace NexaConnect.Services.Order.Application.Orders;

public interface IQrMenuPort
{
    Task<QrGuestContext?> ContextAsync(string capability, CancellationToken ct);
    Task<IReadOnlyList<CatalogMenuItem>> MenuAsync(Guid organization, Guid branch, CancellationToken ct);
}

public sealed class QrMenuService(IQrMenuPort port, IBranchPricingPort policies, TimeProvider time)
{
    public async Task<QrMenu> MenuAsync(string capability, CancellationToken ct)
    {
        var context = await ContextAsync(capability, ct);
        var items = await Items(context, ct);
        await Revalidate(capability, context, ct);
        return new(items.Select(i => new QrMenuItem(i.ProductId, i.Name, i.UnitPrice, i.Currency, i.Available)).ToArray());
    }

    public async Task<QrQuote> QuoteAsync(string capability, QrCart cart, CancellationToken ct)
    {
        var context = await ContextAsync(capability, ct);
        if (cart?.Lines is null || cart.Lines.Count is < 1 or > 50
            || cart.Lines.Any(l => l is null || l.ProductId == Guid.Empty || l.Quantity is < 1 or > 99)
            || cart.Lines.Select(l => l.ProductId).Distinct().Count() != cart.Lines.Count)
            throw new ArgumentException("Invalid cart.");
        var items = await Items(context, ct);
        if (cart.Lines.Any(l => !items.Any(i => i.ProductId == l.ProductId && i.Available)))
            throw new PricingChangedException();
        var policy = await policies.GetAsync(context.OrganizationId, context.RestaurantId, context.BranchId, ct);
        var command = new PlaceOrderCommand(context.OrganizationId, context.BranchId,
            cart.Lines.Select(l => new PlaceOrderLine(l.ProductId, l.Quantity)).ToArray(), context.Currency, "cash", context.RestaurantId);
        var quote = OrderPricingService.Resolve(command, policy, items.ToDictionary(i => i.ProductId));
        await Revalidate(capability, context, ct);
        var pricing = quote.Pricing;
        return new(quote.Fingerprint, context.Currency, Min(time.GetUtcNow().AddMinutes(2), context.ExpiresAtUtc),
            quote.Lines.Select(l => new QrQuoteLine(l.ProductId, l.Name, l.UnitPrice, l.Quantity, l.UnitPrice * l.Quantity)).ToArray(),
            pricing.MenuAmount, pricing.SubtotalAmount, pricing.ServiceChargeAmount, pricing.TaxAmount, pricing.TotalAmount,
            pricing.TaxPercent, pricing.TaxInclusive, pricing.ServiceChargePercent);
    }

    public async Task<QrGuestContext> ContextAsync(string capability, CancellationToken ct)
    {
        if (capability?.Length != 43 || !capability.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
            throw new UnauthorizedAccessException();
        var value = await port.ContextAsync(capability, ct);
        if (value is null || value.ExpiresAtUtc <= time.GetUtcNow() || value.OrganizationId == Guid.Empty
            || value.RestaurantId == Guid.Empty || value.BranchId == Guid.Empty) throw new UnauthorizedAccessException();
        if (value.Currency != "THB") throw new ArgumentException("Unsupported branch currency.");
        return value;
    }
    private async Task Revalidate(string capability, QrGuestContext original, CancellationToken ct)
    {
        var current = await ContextAsync(capability, ct);
        if (current != original) throw new UnauthorizedAccessException();
    }
    private async Task<IReadOnlyList<CatalogMenuItem>> Items(QrGuestContext context, CancellationToken ct)
    {
        var items = await port.MenuAsync(context.OrganizationId, context.BranchId, ct);
        if (items.Count > 200 || items.Any(i => i.ProductId == Guid.Empty || string.IsNullOrWhiteSpace(i.Name)
            || i.Name.Length > 200 || i.Currency != context.Currency || i.UnitPrice < 0 || i.UnitPrice > 99999999m
            || decimal.Round(i.UnitPrice, 2) != i.UnitPrice) || items.Select(i => i.ProductId).Distinct().Count() != items.Count)
            throw new HttpRequestException("Menu cannot be safely displayed.");
        return items;
    }
    private static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b) => a < b ? a : b;
}
