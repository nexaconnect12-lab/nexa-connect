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
    public async Task ReleaseAsync(Guid orderId, Guid branchId, CancellationToken cancellationToken)
    {
        using var response = await client.PostAsync($"api/inventory/v1/branches/{branchId:D}/reservations/{orderId:D}/release", null, cancellationToken);
        if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.NotFound)
            throw new InvalidOperationException($"Inventory release failed with {(int)response.StatusCode}.");
    }
    public async Task<InventoryReservationResult> ReserveAsync(
        Guid orderId, Guid branchId, IReadOnlyCollection<OrderLine> lines, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            $"api/inventory/v1/branches/{branchId:D}/reservations",
            new ReservationRequest(orderId, lines.Select(line => new ReservationLine(line.ProductId, line.Quantity)).ToArray()),
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            string reason = await response.Content.ReadAsStringAsync(cancellationToken);
            return new InventoryReservationResult(false, null, string.IsNullOrWhiteSpace(reason) ? response.StatusCode.ToString() : reason);
        }
        ReservationResponse? reservation = await response.Content.ReadFromJsonAsync<ReservationResponse>(cancellationToken);
        return reservation is null
            ? new InventoryReservationResult(false, null, "Inventory returned an empty reservation response.")
            : new InventoryReservationResult(true, reservation.ReservationId, null);
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
            return new PaymentResult(false, null, await response.Content.ReadAsStringAsync(cancellationToken));
        PaymentResponse? payment = await response.Content.ReadFromJsonAsync<PaymentResponse>(cancellationToken);
        if (payment is null) return new PaymentResult(false, null, "Payment returned an empty response.");
        using var authorize = new HttpRequestMessage(HttpMethod.Post, $"api/payment/v1/intents/{payment.Id:D}/authorize");
        authorize.Headers.TryAddWithoutValidation(TenantContextHeaders.OrganizationId, organizationId.ToString("D"));
        authorize.Headers.TryAddWithoutValidation(TenantContextHeaders.ApplicationCode, "nexa_connect");
        HttpResponseMessage authorization;
        try
        {
            authorization = await client.SendAsync(authorize, cancellationToken);
        }
        catch (HttpRequestException)
        {
            return new PaymentResult(false, payment.Id, "Payment provider outcome is unknown.", "unknown");
        }
        using (authorization)
        {
            if (!authorization.IsSuccessStatusCode)
                return new PaymentResult(false, payment.Id, $"Payment authorization failed with {(int)authorization.StatusCode}.",
                    (int)authorization.StatusCode >= 500 ? "unknown" : "failed");
            PaymentResponse? authorized = await authorization.Content.ReadFromJsonAsync<PaymentResponse>(cancellationToken);
            if (authorized?.Status != "authorized")
                return new PaymentResult(false, payment.Id, authorized?.FailureCode ?? "Payment authorization was not approved.",
                    authorized?.Status is "authorizing" or "unknown" or "requires_action" ? authorized.Status : "failed");
            using var capture = new HttpRequestMessage(HttpMethod.Post, $"api/payment/v1/intents/{payment.Id:D}/capture");
            capture.Headers.TryAddWithoutValidation(TenantContextHeaders.OrganizationId, organizationId.ToString("D"));
            capture.Headers.TryAddWithoutValidation(TenantContextHeaders.ApplicationCode, "nexa_connect");
            try
            {
                using HttpResponseMessage captureResponse = await client.SendAsync(capture, cancellationToken);
                if (!captureResponse.IsSuccessStatusCode)
                    return new PaymentResult(false, payment.Id, $"Payment capture failed with {(int)captureResponse.StatusCode}.",
                        (int)captureResponse.StatusCode >= 500 ? "unknown" : "failed");
                PaymentResponse? captured = await captureResponse.Content.ReadFromJsonAsync<PaymentResponse>(cancellationToken);
                return captured?.Status == "captured"
                    ? new PaymentResult(true, captured.Id, null, "captured")
                    : new PaymentResult(false, payment.Id, captured?.FailureCode ?? "Payment capture was not completed.",
                        captured?.Status is "capturing" or "capture_unknown" ? "unknown" : "failed");
            }
            catch (HttpRequestException)
            {
                return new PaymentResult(false, payment.Id, "Payment capture outcome is unknown.", "unknown");
            }
        }
    }

    private sealed record PaymentRequest(Guid RestaurantId, Guid BranchId, Guid OrderId, string IdempotencyKey,
        decimal Amount, string Currency, string PaymentMethod);
    private sealed record PaymentResponse(Guid Id, string Status, string? FailureCode = null);
}
