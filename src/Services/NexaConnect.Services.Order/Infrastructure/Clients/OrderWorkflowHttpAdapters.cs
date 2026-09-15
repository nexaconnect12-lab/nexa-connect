using System.Net.Http.Json;
using NexaConnect.Services.Order.Application.Workflow;
using NexaConnect.Services.Order.Domain;
using NexaConnect.Contracts.Platform;

namespace NexaConnect.Services.Order.Infrastructure.Clients;

public sealed class HttpMenuCatalogPort(HttpClient client) : IMenuCatalogPort
{
    public async Task<IReadOnlyDictionary<Guid, CatalogMenuItem>> GetItemsAsync(
        Guid branchId, IReadOnlyCollection<Guid> productIds, CancellationToken cancellationToken)
    {
        IReadOnlyCollection<MenuItemResponse> response = await client.GetFromJsonAsync<IReadOnlyCollection<MenuItemResponse>>(
            $"api/catalog/v1/branches/{branchId:D}/menu-items", cancellationToken) ?? [];
        return response.Where(item => productIds.Contains(item.ProductId)).ToDictionary(
            item => item.ProductId,
            item => new CatalogMenuItem(item.ProductId, item.Name, item.UnitPrice, item.Currency, item.Available, item.PreparationStation));
    }

    private sealed record MenuItemResponse(Guid ProductId, string Name, decimal UnitPrice, string Currency, string PreparationStation, bool Available);
}

public sealed class HttpInventoryReservationPort(HttpClient client) : IInventoryReservationPort
{
    public async Task ReleaseAsync(Guid organizationId, Guid orderId, Guid branchId, CancellationToken cancellationToken)
    {
        using var request = TenantRequest(HttpMethod.Post,
            $"api/inventory/v1/branches/{branchId:D}/reservations/{orderId:D}/release", organizationId);
        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.NotFound)
            throw new InvalidOperationException($"Inventory release failed with {(int)response.StatusCode}.");
    }
    public async Task<InventoryReservationResult> ReserveAsync(
        Guid organizationId, Guid orderId, Guid branchId, IReadOnlyCollection<OrderLine> lines, CancellationToken cancellationToken)
    {
        using var request = TenantRequest(HttpMethod.Post,
            $"api/inventory/v1/branches/{branchId:D}/reservations", organizationId);
        request.Content = JsonContent.Create(
            new ReservationRequest(orderId, lines.Select(line => new ReservationLine(line.ProductId, line.Quantity)).ToArray()));
        using HttpResponseMessage response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            if ((int)response.StatusCode >= 500)
                throw new HttpRequestException("Inventory reservation dependency failed.", null, response.StatusCode);
            return new InventoryReservationResult(false, null, $"Inventory reservation was rejected with {(int)response.StatusCode}.");
        }
        ReservationResponse? reservation = await response.Content.ReadFromJsonAsync<ReservationResponse>(cancellationToken);
        return reservation is null
            ? new InventoryReservationResult(false, null, "Inventory returned an empty reservation response.")
            : new InventoryReservationResult(true, reservation.ReservationId, null);
    }

    private static HttpRequestMessage TenantRequest(HttpMethod method, string path, Guid organizationId)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.TryAddWithoutValidation(TenantContextHeaders.OrganizationId, organizationId.ToString("D"));
        request.Headers.TryAddWithoutValidation(TenantContextHeaders.ApplicationCode, "nexa_connect");
        return request;
    }

    private sealed record ReservationRequest(Guid OrderId, IReadOnlyCollection<ReservationLine> Lines);
    private sealed record ReservationLine(Guid ProductId, decimal Quantity);
    private sealed record ReservationResponse(Guid ReservationId, Guid OrderId, Guid BranchId, IReadOnlyCollection<ReservationLine> Lines);
}

public sealed class HttpKitchenPort(HttpClient client) : IKitchenPort
{
    public async Task CancelTicketAsync(Guid organizationId,Guid orderId, Guid branchId, CancellationToken cancellationToken)
    {
        using var request=new HttpRequestMessage(HttpMethod.Post,$"api/kitchen/v1/tickets/{orderId:D}/cancel?branchId={branchId:D}");request.Headers.TryAddWithoutValidation(TenantContextHeaders.OrganizationId,organizationId.ToString("D"));request.Headers.TryAddWithoutValidation(TenantContextHeaders.ApplicationCode,"nexa_connect");using var response = await client.SendAsync(request,cancellationToken);
        if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.NotFound)
            throw new InvalidOperationException($"Kitchen cancellation failed with {(int)response.StatusCode}.");
    }
    public async Task<KitchenTicketResult> CreateTicketAsync(
        Guid organizationId,Guid restaurantId,Guid orderId, Guid branchId, IReadOnlyCollection<OrderLine> lines, CancellationToken cancellationToken)
    {
        KitchenTicketResult? firstTicket = null;
        foreach (IGrouping<string, OrderLine> group in lines.GroupBy(line => line.PreparationStation, StringComparer.OrdinalIgnoreCase))
        {
            using var request=new HttpRequestMessage(HttpMethod.Post,"api/kitchen/v1/tickets"){Content=JsonContent.Create(new TicketRequest(restaurantId,orderId, branchId, group.Select(line => new TicketLine(line.ProductId, line.Name, line.Quantity, line.PreparationStation)).ToArray()))};request.Headers.TryAddWithoutValidation(TenantContextHeaders.OrganizationId,organizationId.ToString("D"));request.Headers.TryAddWithoutValidation(TenantContextHeaders.ApplicationCode,"nexa_connect");using HttpResponseMessage response=await client.SendAsync(request,cancellationToken);
            response.EnsureSuccessStatusCode();
            TicketResponse ticket = await response.Content.ReadFromJsonAsync<TicketResponse>(cancellationToken)
                ?? throw new InvalidOperationException("Kitchen returned an empty ticket response.");
            firstTicket ??= new KitchenTicketResult(ticket.TicketId);
        }

        return firstTicket ?? throw new InvalidOperationException("Kitchen requires at least one order line.");
    }

    private sealed record TicketRequest(Guid RestaurantId,Guid OrderId, Guid BranchId, IReadOnlyCollection<TicketLine> Lines);
    private sealed record TicketLine(Guid ProductId, string Name, int Quantity, string PreparationStation);
    private sealed record TicketResponse(Guid TicketId);
}

public sealed class HttpPaymentPort(HttpClient client) : IPaymentPort
{
    public async Task<PaymentResult> AuthorizeAsync(
        Guid organizationId, Guid restaurantId, Guid branchId, Guid orderId, decimal amount, string currency, string method,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/payment/v1/intents")
        {
            Content = JsonContent.Create(new PaymentRequest(restaurantId, branchId, orderId, $"order:{orderId:D}", amount, currency, method))
        };
        request.Headers.TryAddWithoutValidation(TenantContextHeaders.OrganizationId, organizationId.ToString("D"));
        request.Headers.TryAddWithoutValidation(TenantContextHeaders.ApplicationCode, "nexa_connect");
        using HttpResponseMessage response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Payment intent creation failed with {(int)response.StatusCode}.", null,
                response.StatusCode);
        PaymentResponse payment = await ReadRequiredAsync(response, "creation", cancellationToken);
        return await ResumeAsync(organizationId, payment, cancellationToken);
    }

    private async Task<PaymentResult> ResumeAsync(Guid organizationId, PaymentResponse payment,
        CancellationToken cancellationToken)
    {
        string status = Normalize(payment.Status);
        if (status == "pending")
        {
            PaymentResponse? authorized = await PostAndReconcileAsync(
                organizationId, payment.Id, "authorize", "authorization", cancellationToken);
            if (authorized is null)
                return Uncertain(payment.Id, "Payment authorization outcome is unknown.");
            status = Normalize(authorized.Status);
            payment = authorized;
        }

        if (status == "authorized")
        {
            PaymentResponse? captured = await PostAndReconcileAsync(
                organizationId, payment.Id, "capture", "capture", cancellationToken);
            if (captured is null)
                return Uncertain(payment.Id, "Payment capture outcome is unknown.");
            payment = captured;
            status = Normalize(captured.Status);
        }

        return status switch
        {
            "captured" => new PaymentResult(true, payment.Id, null, "captured"),
            "failed" or "voided" or "void_failed" =>
                new PaymentResult(false, payment.Id, payment.FailureCode ?? "Payment was not completed.", "failed"),
            "authorizing" or "unknown" or "requires_action" or "capturing" or "capture_unknown"
                or "voiding" or "void_unknown" =>
                new PaymentResult(false, payment.Id,
                    payment.FailureCode ?? "Payment requires server-side reconciliation.", status),
            _ => throw new InvalidOperationException($"Payment intent {payment.Id} returned unsupported status '{payment.Status}'.")
        };
    }

    private async Task<PaymentResponse?> PostAndReconcileAsync(Guid organizationId, Guid paymentId, string action,
        string operation, CancellationToken cancellationToken)
    {
        using var request = TenantRequest(HttpMethod.Post, $"api/payment/v1/intents/{paymentId:D}/{action}", organizationId);
        try
        {
            using HttpResponseMessage response = await client.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
                return await ReadRequiredAsync(response, operation, cancellationToken);
        }
        catch (HttpRequestException)
        {
            return null;
        }

        // A state-changing response may be lost or race another worker. Read the
        // authoritative intent before deciding whether another operation is safe.
        return await ReadCurrentAsync(organizationId, paymentId, cancellationToken);
    }

    private async Task<PaymentResponse> ReadCurrentAsync(Guid organizationId, Guid paymentId,
        CancellationToken cancellationToken)
    {
        using var request = TenantRequest(HttpMethod.Get, $"api/payment/v1/intents/{paymentId:D}", organizationId);
        using HttpResponseMessage response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Payment intent reconciliation failed with {(int)response.StatusCode}.", null,
                response.StatusCode);
        return await ReadRequiredAsync(response, "reconciliation", cancellationToken);
    }

    private static async Task<PaymentResponse> ReadRequiredAsync(HttpResponseMessage response, string operation,
        CancellationToken cancellationToken) =>
        await response.Content.ReadFromJsonAsync<PaymentResponse>(cancellationToken)
        ?? throw new InvalidOperationException($"Payment returned an empty {operation} response.");

    private static HttpRequestMessage TenantRequest(HttpMethod method, string path, Guid organizationId)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.TryAddWithoutValidation(TenantContextHeaders.OrganizationId, organizationId.ToString("D"));
        request.Headers.TryAddWithoutValidation(TenantContextHeaders.ApplicationCode, "nexa_connect");
        return request;
    }

    private static PaymentResult Uncertain(Guid paymentId, string reason) =>
        new(false, paymentId, reason, "unknown");

    private static string Normalize(string status) => status.Trim().ToLowerInvariant();

    private sealed record PaymentRequest(Guid RestaurantId, Guid BranchId, Guid OrderId, string IdempotencyKey,
        decimal Amount, string Currency, string PaymentMethod);
    private sealed record PaymentResponse(Guid Id, string Status, string? FailureCode = null);
}
