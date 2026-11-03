using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using NexaConnect.Contracts.RestaurantQr;

namespace NexaConnect.POS;

public sealed partial class PosApiClient
{
    // Translate the owning context's transport DTOs into transient cashier presentation data.
    public async Task<PosQrRequestPage> ListQrRequestsAsync(PosTokenSet token, string state, CancellationToken ct = default)
    {
        if (!PosQrReviewState.ValidFilter(state)) throw new ArgumentException("Invalid QR request filter.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(20));
        using var request = CreateRequest(HttpMethod.Get, QrReviewPath + "?state=" + state, token);
        AddTenantContext(request, _configuration.OrganizationId);
        using var response = await SendCheckoutAsync(_orderHttpClient, request, "order.qr.review.list", Guid.NewGuid(), deadline.Token, true);
        if (!response.IsSuccessStatusCode) throw new PosApiException((int)response.StatusCode, "QR requests unavailable.");
        await BoundQrResponseAsync(response, deadline.Token);
        var page = await response.Content.ReadFromJsonAsync<QrRequestList>(deadline.Token) ?? throw new InvalidDataException();
        if (page.Items is null || page.Items.Count > 10 || page.Items.Select(i => i?.Id).Distinct().Count() != page.Items.Count)
            throw new InvalidDataException("Invalid QR request page.");
        return new(page.Items.Select(i => PosQrRequest.From(i, _configuration.Currency)).ToArray(), page.HasMore);
    }

    public async Task<PosQrRequest> DecideQrRequestAsync(PosTokenSet token, Guid id, long version,
        string? rejectionReason = null, CancellationToken ct = default)
    {
        if (id == Guid.Empty || version < 1 || rejectionReason is not (null or "unavailable" or "closing" or "staff_review"))
            throw new ArgumentException("Invalid QR decision.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(20));
        using var request = CreateRequest(HttpMethod.Post, QrReviewPath + $"/{id:D}/" + (rejectionReason is null ? "accept" : "reject"), token);
        AddTenantContext(request, _configuration.OrganizationId);
        request.Content = rejectionReason is null ? JsonContent.Create(new QrAcceptance(version)) : JsonContent.Create(new QrRejection(version, rejectionReason));
        // Never automatically replay a command. Reload the original queue after response loss.
        using var response = await SendCheckoutAsync(_orderHttpClient, request, "order.qr.review.decision", Guid.NewGuid(), deadline.Token, true);
        if (!response.IsSuccessStatusCode) throw new PosApiException((int)response.StatusCode, "Reload the original QR request before another decision.");
        await BoundQrResponseAsync(response, deadline.Token);
        var value = await response.Content.ReadFromJsonAsync<QrRequestView>(deadline.Token) ?? throw new InvalidDataException();
        var result = PosQrRequest.From(value, _configuration.Currency);
        if (result.Id != id || result.Version < version || (rejectionReason is null
                ? result.Status is not ("accepted" or "price_review") : result.Status != "rejected"))
            throw new InvalidDataException("QR decision does not match its original request.");
        return result;
    }

    private string QrReviewPath => $"api/order/v1/customer/organizations/{_configuration.OrganizationId:D}/branches/{_configuration.BranchId:D}/qr-requests";
    private static async Task BoundQrResponseAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.Content.Headers.ContentLength > 2097152 || response.Content.Headers.ContentType?.MediaType != "application/json")
            throw new InvalidDataException("Invalid QR response.");
        await response.Content.LoadIntoBufferAsync(2097152, ct);
    }
}
