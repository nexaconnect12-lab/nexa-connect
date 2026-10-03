namespace NexaConnect.Services.Order.Domain;

public sealed record PricingPolicy(long Version, decimal TaxPercent, bool TaxInclusive, decimal ServiceChargePercent);

public sealed record OrderPricing(long PolicyVersion, decimal TaxPercent, bool TaxInclusive,
    decimal ServiceChargePercent, decimal MenuAmount, decimal SubtotalAmount,
    decimal ServiceChargeAmount, decimal TaxAmount, decimal TotalAmount)
{
    public static OrderPricing Calculate(IReadOnlyCollection<OrderLine> lines, string currency, PricingPolicy policy)
    {
        if (currency != "THB") throw new ArgumentException("Priced checkout supports THB only.");
        if (policy.Version <= 0 || policy.TaxPercent is < 0 or > 100 || policy.ServiceChargePercent is < 0 or > 100
            || decimal.Round(policy.TaxPercent, 2) != policy.TaxPercent
            || decimal.Round(policy.ServiceChargePercent, 2) != policy.ServiceChargePercent)
            throw new ArgumentException("Invalid branch pricing policy.");
        if (lines.Count is 0 or > 200 || lines.Any(l => l.Quantity is <= 0 or > 10000 || l.UnitPrice < 0
            || decimal.Round(l.UnitPrice, 2) != l.UnitPrice))
            throw new ArgumentException("Invalid priced order lines.");
        decimal menu = Round(lines.Sum(l => checked(l.UnitPrice * l.Quantity)));
        if (menu <= 0 || menu > 99999999m) throw new ArgumentException("Order amount is outside the supported range.");
        decimal subtotal = policy.TaxInclusive ? Round(menu / (1 + policy.TaxPercent / 100)) : menu;
        decimal service = Round(subtotal * policy.ServiceChargePercent / 100);
        // Preserve inclusive menu prices exactly; separately round tax on the added service charge.
        decimal tax = policy.TaxInclusive
            ? menu - subtotal + Round(service * policy.TaxPercent / 100)
            : Round((subtotal + service) * policy.TaxPercent / 100);
        return new(policy.Version, policy.TaxPercent, policy.TaxInclusive, policy.ServiceChargePercent,
            menu, subtotal, service, tax, subtotal + service + tax);
    }

    private static decimal Round(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
}
