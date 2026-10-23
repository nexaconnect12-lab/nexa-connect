namespace NexaConnect.POS;

// An allow-listed, immutable presentation projection. No identifiers, credentials,
// staff messages, receipt lookups or command/recovery objects cross this boundary.
public sealed record CustomerDisplayLine(string Name, int Quantity, decimal UnitPrice)
{
    public decimal LineTotal => Quantity * UnitPrice;
}

public sealed record CustomerDisplaySnapshot(string Heading, string Message,
    IReadOnlyList<CustomerDisplayLine> Lines, string Currency = "", decimal? Subtotal = null,
    decimal? ServiceCharge = null, decimal? Tax = null, decimal? Total = null,
    string TotalLabel = "", string Tender = "")
{
    public static CustomerDisplaySnapshot Welcome { get; } = new("Welcome", "Thank you for visiting.", []);
    public static CustomerDisplaySnapshot Paused { get; } = new("Please wait", "Your cashier will be with you shortly.", []);
}

public sealed class CustomerDisplayState
{
    private CustomerDisplaySnapshot current = CustomerDisplaySnapshot.Welcome;
    public CustomerDisplaySnapshot Visible(bool signedIn, bool locked) =>
        locked ? CustomerDisplaySnapshot.Paused : signedIn ? current : CustomerDisplaySnapshot.Welcome;

    public void Cart(string currency, IEnumerable<CustomerDisplayLine> lines)
    {
        var copy = Array.AsReadOnly(lines.Select(line => line with { }).ToArray());
        current = copy.Count == 0 ? CustomerDisplaySnapshot.Welcome
            : new("Your order", "Estimate only. Your cashier will confirm server pricing.", copy,
                currency, Total: copy.Sum(line => line.LineTotal), TotalLabel: "Cart estimate");
    }

    public void Quote(PosOrderQuote quote, string currency) => current = new(
        "Review your order", "Server quote. Prices are confirmed when the order is accepted.",
        Array.AsReadOnly(quote.Lines.Select(line => new CustomerDisplayLine(line.Name, line.Quantity, line.UnitPrice)).ToArray()),
        currency, quote.Pricing.SubtotalAmount, quote.Pricing.ServiceChargeAmount,
        quote.Pricing.TaxAmount, quote.Pricing.TotalAmount, "Quoted total");

    public void Order(PosOrderResult order, string tender)
    {
        if (order.Status == PosOrderStatus.Paid) { Complete(); return; }
        if (order.Status == PosOrderStatus.Cancelled) { Cancelled(); return; }
        if (order.Status is PosOrderStatus.Rejected or PosOrderStatus.PaymentFailed)
        { current = new("Order not completed", "Please speak with your cashier.", []); return; }
        var p = order.Pricing;
        current = current with {
            Heading = "Payment pending", Message = "Payment has not been confirmed. Please follow your cashier's guidance.",
            Currency = order.Currency, Subtotal = p?.SubtotalAmount, ServiceCharge = p?.ServiceChargeAmount,
            Tax = p?.TaxAmount, Total = order.TotalAmount, TotalLabel = "Order total", Tender = TenderLabel(tender)
        };
    }

    public void Tender(string tender) => current = current with { Tender = TenderLabel(tender) };
    public void Unconfirmed() => current = current with {
        Heading = "Please wait", Message = "The outcome needs verification. Please do not pay again."
    };
    public void PricingChanged() => current = new("Pricing review required", "Please wait while your cashier reviews current prices.", []);
    public void CancellationPending() => current = current with {
        Heading = "Please wait", Message = "Cancellation needs verification. Please speak with your cashier."
    };
    // Completion/cancellation immediately remove customer transaction details.
    public void Complete() => current = new("Payment confirmed", "Thank you. Your payment is complete.", []);
    public void Cancelled() => current = new("Order cancelled", "Please speak with your cashier if you need assistance.", []);
    public void Clear() => current = CustomerDisplaySnapshot.Welcome;
    private static string TenderLabel(string method) => method switch {
        "cash" or "cash_manual" => "Cash", "promptpay_manual" => "PromptPay (manual confirmation)",
        "card_omise_test" => "Test card", _ => ""
    };
}
