using System.Net;
using System.Net.Http.Headers;
using NexaConnect.Contracts.Reporting;
using NexaConnect.Infrastructure.Authentication;
using NexaConnect.Services.Reporting.Application;

namespace NexaConnect.Services.Reporting.Infrastructure;

public sealed class HttpEndOfDaySources(IHttpClientFactory clients, IServiceWorkloadTokenProvider tokens) : IEndOfDaySources
{
    public async Task<BranchBusinessCalendar?> CalendarAsync(Guid branch, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/restaurant/v1/branches/{branch:D}/business-calendar");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await tokens.GetAsync(ct));
        using var response = await clients.CreateClient("ReportingRestaurant").SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<BranchBusinessCalendar>(cancellationToken: ct);
    }
    public Task<OrderDaySummary> OrderAsync(EndOfDayWindow window, string bearer, CancellationToken ct) => Read<OrderDaySummary>("Order", "order", window, bearer, ct);
    public Task<PaymentDaySummary> PaymentAsync(EndOfDayWindow window, string bearer, CancellationToken ct) => Read<PaymentDaySummary>("Payment", "payment", window, bearer, ct);
    public Task<PosDaySummary> PosAsync(EndOfDayWindow window, string bearer, CancellationToken ct) => Read<PosDaySummary>("POS", "pos", window, bearer, ct);
    private async Task<T> Read<T>(string client, string service, EndOfDayWindow w, string bearer, CancellationToken ct)
    {
        string query = $"organizationId={w.OrganizationId:D}&restaurantId={w.RestaurantId:D}&branchId={w.BranchId:D}&fromUtc={Uri.EscapeDataString(w.FromUtc.ToString("O"))}&toUtc={Uri.EscapeDataString(w.ToUtc.ToString("O"))}";
        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/{service}/v1/customer/end-of-day?{query}");
        request.Headers.Authorization = AuthenticationHeaderValue.Parse(bearer);
        using var response = await clients.CreateClient("EndOfDay" + client).SendAsync(request, ct);
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized) throw new UnauthorizedAccessException();
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken: ct) ?? throw new InvalidDataException("Source unavailable.");
    }
}
