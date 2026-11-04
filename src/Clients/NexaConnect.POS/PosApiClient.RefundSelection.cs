using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;

namespace NexaConnect.POS;

public sealed record PosRefundPayment(Guid PaymentIntentId, Guid OrderId, string OrderNumber, string ReceiptNumber,
    DateTimeOffset PaidAtUtc, string Currency, decimal CapturedAmount, decimal AvailableAmount, string Items)
{
    public string Label => $"{OrderNumber} · {PaidAtUtc.LocalDateTime:g} · {Currency} {CapturedAmount:N2}";
}

public sealed partial class PosApiClient
{
    private sealed record RefundOrder(Guid OrderId, Guid OrganizationId, Guid BranchId, string Status,
        decimal TotalAmount, string Currency, Guid? PaymentIntentId);
    private sealed record RefundIntent(Guid Id, Guid OrganizationId, Guid RestaurantId, Guid BranchId, Guid OrderId,
        decimal Amount, string Currency, string PaymentMethod, string Status);

    // Read-only discovery. Payment's transactional reservation/authorization remains the refund authority.
    public async Task<PosRefundPayment> FindRefundPaymentAsync(PosTokenSet token, Guid id, bool byOrder,
        CancellationToken ct = default)
    {
        if (id == Guid.Empty) throw new ArgumentException("Select an original payment.");
        if (string.IsNullOrWhiteSpace(_configuration.PaymentApi)) throw new InvalidOperationException("Payment service is not configured.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(20)); ct = deadline.Token;
        var correlation = Guid.NewGuid();
        try
        {
            RefundOrder? order = byOrder ? await RefundRead<RefundOrder>(_orderHttpClient, $"api/order/v1/orders/{id:D}", token, correlation, ct) : null;
            if (byOrder && (order!.OrderId != id || order.PaymentIntentId is null || order.PaymentIntentId == Guid.Empty)) throw new InvalidOperationException("This order has no provider card payment. Cash and manual PromptPay refunds use a separate workflow.");
            Guid paymentId = byOrder ? order!.PaymentIntentId!.Value : id;
            var intent = await RefundRead<RefundIntent>(_paymentHttpClient, $"api/payment/v1/intents/{paymentId:D}", token, correlation, ct);
            if (intent.Id != paymentId || intent.OrderId == Guid.Empty || intent.OrganizationId != _configuration.OrganizationId
                || intent.RestaurantId != _configuration.RestaurantId || intent.BranchId != _configuration.BranchId
                || intent.Currency != _configuration.Currency || intent.Amount <= 0 || intent.Status != "captured"
                || intent.PaymentMethod is not ("card_omise_test" or "card_omise" or "card"))
                throw new InvalidDataException("Select a captured payment in this branch and currency.");
            order ??= await RefundRead<RefundOrder>(_orderHttpClient, $"api/order/v1/orders/{intent.OrderId:D}", token, correlation, ct);
            if (order.OrderId != intent.OrderId || order.OrganizationId != intent.OrganizationId || order.BranchId != intent.BranchId
                || order.PaymentIntentId != intent.Id || order.Status != "Paid" || order.TotalAmount != intent.Amount || order.Currency != intent.Currency)
                throw new InvalidDataException("Payment does not match its original paid order.");
            var receipt = await GetReceiptAsync(token, order.OrderId, ct);
            // Order's existing Omise adapter maps its sandbox tender to Payment's generic card method.
            bool matchingTender = receipt.Tender == intent.PaymentMethod
                || intent.PaymentMethod == "card" && receipt.Tender == "card_omise_test";
            if (receipt.TotalAmount != intent.Amount || receipt.Currency != intent.Currency || !matchingTender
                || string.IsNullOrWhiteSpace(receipt.OrderNumber) || receipt.OrderNumber.Length > 128 || receipt.OrderNumber.Any(char.IsControl)
                || receipt.Lines.Any(l => string.IsNullOrWhiteSpace(l.Name) || l.Name.Length > 256 || l.Name.Any(char.IsControl)))
                throw new InvalidDataException("Original card receipt could not be verified.");
            var refunds = await RefundRead<PosPaymentRefund[]>(_paymentHttpClient, $"api/payment/v1/intents/{intent.Id:D}/refunds", token, correlation, ct);
            if (refunds.Length > 1000 || refunds.Any(r => r is null || r.Id == Guid.Empty || r.OperationId == Guid.Empty)
                || refunds.Select(r => r.Id).Distinct().Count() != refunds.Length
                || refunds.Select(r => r.OperationId).Distinct().Count() != refunds.Length) throw new InvalidDataException("Refund history is not complete.");
            foreach (var refund in refunds)
            {
                ValidateRefund(refund, intent.Id, refund.OperationId);
                if (refund.OrderId != order.OrderId) throw new InvalidDataException("Refund history differs from the original order.");
            }
            decimal reserved = refunds.Where(r => r.Status != "failed").Sum(r => r.Amount);
            if (reserved > intent.Amount) throw new InvalidDataException("Refund balance needs manager review.");
            return new(intent.Id, order.OrderId, receipt.OrderNumber, receipt.ReceiptNumber, receipt.PaidAtUtc, intent.Currency,
                intent.Amount, intent.Amount - reserved, string.Join(", ", receipt.Lines.Take(3).Select(l => $"{l.Quantity} × {l.Name}"))
                    + (receipt.Lines.Count > 3 ? " …" : ""));
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            loggerFactory.CreateLogger("NexaConnect.POS.Checkout").LogWarning(
                "Refund selection unavailable {CorrelationId}", correlation);
            throw;
        }
    }
    private async Task<T> RefundRead<T>(HttpClient client, string path, PosTokenSet token, Guid correlation, CancellationToken ct)
    {
        using var request = CreateRequest(HttpMethod.Get, path, token); AddTenantContext(request, _configuration.OrganizationId);
        using var response = await SendCheckoutAsync(client, request, "payment.refund.selection", correlation, ct, true);
        await response.Content.LoadIntoBufferAsync(131072, ct);
        await EnsureSuccessAsync(response, "Original payment unavailable or access denied. Refresh before retrying.");
        return await response.Content.ReadFromJsonAsync<T>(ct) ?? throw new InvalidDataException("Empty payment selection response.");
    }
}
