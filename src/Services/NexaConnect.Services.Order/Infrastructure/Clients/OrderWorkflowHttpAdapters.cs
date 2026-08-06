using System.Net.Http.Json;
using NexaConnect.Services.Order.Application.Workflow;
using NexaConnect.Services.Order.Domain;

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
    public async Task CancelTicketAsync(Guid orderId, Guid branchId, CancellationToken cancellationToken)
    {
        using var response = await client.PostAsync($"api/kitchen/v1/tickets/{orderId:D}/cancel", null, cancellationToken);
        if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.NotFound)
            throw new InvalidOperationException($"Kitchen cancellation failed with {(int)response.StatusCode}.");
    }
    public async Task<KitchenTicketResult> CreateTicketAsync(
        Guid orderId, Guid branchId, IReadOnlyCollection<OrderLine> lines, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "api/kitchen/v1/tickets",
            new TicketRequest(orderId, branchId, lines.Select(line => new TicketLine(line.ProductId, line.Name, line.Quantity, line.PreparationStation)).ToArray()),
            cancellationToken);
        response.EnsureSuccessStatusCode();
        TicketResponse ticket = await response.Content.ReadFromJsonAsync<TicketResponse>(cancellationToken)
            ?? throw new InvalidOperationException("Kitchen returned an empty ticket response.");
        return new KitchenTicketResult(ticket.TicketId);
    }

    private sealed record TicketRequest(Guid OrderId, Guid BranchId, IReadOnlyCollection<TicketLine> Lines);
    private sealed record TicketLine(Guid ProductId, string Name, int Quantity, string PreparationStation);
    private sealed record TicketResponse(Guid TicketId);
}

public sealed class HttpPaymentPort(HttpClient client, IConfiguration configuration) : IPaymentPort
{
    public async Task<PaymentResult> AuthorizeAsync(
        Guid orderId, decimal amount, string currency, string method, CancellationToken cancellationToken)
    {
        Guid restaurantId = configuration.GetValue<Guid>("Workflow:RestaurantId");
        Guid branchId = configuration.GetValue<Guid>("Workflow:BranchId");
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "api/payment/v1/intents",
            new PaymentRequest(restaurantId, branchId, orderId, $"order:{orderId:D}", amount, currency, method),
            cancellationToken);
        if (!response.IsSuccessStatusCode)
            return new PaymentResult(false, null, await response.Content.ReadAsStringAsync(cancellationToken));
        PaymentResponse? payment = await response.Content.ReadFromJsonAsync<PaymentResponse>(cancellationToken);
        return payment is null
            ? new PaymentResult(false, null, "Payment returned an empty response.")
            : new PaymentResult(payment.Status is "authorized" or "captured" or "pending", payment.Id, null);
    }

    private sealed record PaymentRequest(Guid RestaurantId, Guid BranchId, Guid OrderId, string IdempotencyKey,
        decimal Amount, string Currency, string PaymentMethod);
    private sealed record PaymentResponse(Guid Id, string Status);
}
