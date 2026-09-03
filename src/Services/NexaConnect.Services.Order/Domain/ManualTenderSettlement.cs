namespace NexaConnect.Services.Order.Domain;

public enum ManualTenderMethod
{
    Cash,
    PromptPayManual
}

public sealed record ManualTenderSettlement
{
    private ManualTenderSettlement(Guid id, Guid orderId, Guid organizationId, Guid branchId, Guid terminalId,
        Guid idempotencyKey, ManualTenderMethod method, decimal amount, string currency, string operatorSubjectId,
        bool receiptConfirmed, string? bankReference, DateTimeOffset occurredAtUtc)
    {
        Id = id;
        OrderId = orderId;
        OrganizationId = organizationId;
        BranchId = branchId;
        TerminalId = terminalId;
        IdempotencyKey = idempotencyKey;
        Method = method;
        Amount = amount;
        Currency = currency;
        OperatorSubjectId = operatorSubjectId;
        ReceiptConfirmed = receiptConfirmed;
        BankReference = bankReference;
        OccurredAtUtc = occurredAtUtc;
    }

    public Guid Id { get; }
    public Guid OrderId { get; }
    public Guid OrganizationId { get; }
    public Guid BranchId { get; }
    public Guid TerminalId { get; }
    public Guid IdempotencyKey { get; }
    public ManualTenderMethod Method { get; }
    public decimal Amount { get; }
    public string Currency { get; }
    public string OperatorSubjectId { get; }
    public bool ReceiptConfirmed { get; }
    public string? BankReference { get; }
    public DateTimeOffset OccurredAtUtc { get; }

    public static ManualTenderSettlement Create(OrderAggregate order, Guid organizationId, Guid branchId,
        Guid terminalId, Guid idempotencyKey, string method, decimal amount, string currency,
        string operatorSubjectId, bool receiptConfirmed, string? bankReference, DateTimeOffset occurredAtUtc)
    {
        ArgumentNullException.ThrowIfNull(order);
        if (organizationId == Guid.Empty || organizationId != order.OrganizationId)
            throw new ArgumentException("The settlement organization must own the order.", nameof(organizationId));
        if (branchId == Guid.Empty || branchId != order.BranchId)
            throw new ArgumentException("The settlement branch must own the order.", nameof(branchId));
        if (terminalId == Guid.Empty) throw new ArgumentException("Terminal is required.", nameof(terminalId));
        if (idempotencyKey == Guid.Empty) throw new ArgumentException("Idempotency key is required.", nameof(idempotencyKey));
        if (string.IsNullOrWhiteSpace(operatorSubjectId) || operatorSubjectId.Trim().Length > 200)
            throw new ArgumentException("Operator subject is required and must not exceed 200 characters.", nameof(operatorSubjectId));
        if (occurredAtUtc == default) throw new ArgumentException("Settlement time is required.", nameof(occurredAtUtc));
        if (order.Status is not (OrderStatus.KitchenAccepted or OrderStatus.PaymentPending))
            throw new InvalidOperationException("Only an accepted or payment-pending order can be settled manually.");
        if (!string.Equals(currency?.Trim(), order.Currency, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(order.Currency, "THB", StringComparison.Ordinal))
            throw new ArgumentException("Manual tender currently requires the order's exact THB currency.", nameof(currency));
        if (amount <= 0 || amount != order.TotalAmount)
            throw new ArgumentException("Manual tender amount must equal the order total.", nameof(amount));

        ManualTenderMethod parsedMethod = method?.Trim().ToLowerInvariant() switch
        {
            "cash" => ManualTenderMethod.Cash,
            "promptpay_manual" => ManualTenderMethod.PromptPayManual,
            _ => throw new ArgumentException("Manual tender method must be cash or promptpay_manual.", nameof(method))
        };
        string? normalizedReference = string.IsNullOrWhiteSpace(bankReference) ? null : bankReference.Trim();
        if (normalizedReference?.Length > 64)
            throw new ArgumentException("Bank reference must not exceed 64 characters.", nameof(bankReference));
        if (parsedMethod == ManualTenderMethod.PromptPayManual && !receiptConfirmed)
            throw new ArgumentException("PromptPay receipt confirmation is required before settlement.", nameof(receiptConfirmed));
        if (parsedMethod == ManualTenderMethod.Cash && normalizedReference is not null)
            throw new ArgumentException("Cash settlement cannot contain a bank reference.", nameof(bankReference));

        return new ManualTenderSettlement(Guid.NewGuid(), order.Id, organizationId, branchId, terminalId,
            idempotencyKey, parsedMethod, amount, "THB", operatorSubjectId.Trim(), receiptConfirmed,
            normalizedReference, occurredAtUtc);
    }
}
