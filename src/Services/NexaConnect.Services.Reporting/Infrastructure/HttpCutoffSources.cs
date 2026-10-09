using System.Net;
using System.Net.Http.Headers;
using NexaConnect.Contracts.Reporting;
using NexaConnect.Services.Reporting.Application;

namespace NexaConnect.Services.Reporting.Infrastructure;

public sealed class HttpCutoffSources(IHttpClientFactory clients) : ICutoffSources,ISealedSources
{
    public Task<SourceCutoffRead<OrderDaySummary>> OrderAsync(EndOfDayWindow w,Guid id,string bearer,CancellationToken ct)=>Read<OrderDaySummary>("Order",w,id,bearer,ct);
    public Task<SourceCutoffRead<PaymentDaySummary>> PaymentAsync(EndOfDayWindow w,Guid id,string bearer,CancellationToken ct)=>Read<PaymentDaySummary>("Payment",w,id,bearer,ct);
    private async Task<SourceCutoffRead<T>> Read<T>(string service,EndOfDayWindow w,Guid id,string bearer,CancellationToken ct)
    {
        string query=$"organizationId={w.OrganizationId:D}&restaurantId={w.RestaurantId:D}&branchId={w.BranchId:D}&fromUtc={Uri.EscapeDataString(w.FromUtc.ToString("O"))}&toUtc={Uri.EscapeDataString(w.ToUtc.ToString("O"))}";
        using var request=new HttpRequestMessage(HttpMethod.Get,$"api/{service.ToLowerInvariant()}/v1/customer/day-cutoffs/{id:D}?{query}");
        request.Headers.Authorization=AuthenticationHeaderValue.Parse(bearer);
        using var response=await clients.CreateClient("EndOfDay"+service).SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct);
        if(response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)throw new UnauthorizedAccessException();
        response.EnsureSuccessStatusCode();
        return await NexaConnect.Infrastructure.Http.BoundedJson.ReadAsync<SourceCutoffRead<T>>(response,16*1024*1024,ct);
    }
    public Task<SourceSealRead<OrderDaySummary>> OrderSealAsync(EndOfDayWindow w,Guid id,string bearer,CancellationToken ct)=>ReadSeal<OrderDaySummary>("Order",w,id,bearer,ct);
    public Task<SourceSealRead<PaymentDaySummary>> PaymentSealAsync(EndOfDayWindow w,Guid id,string bearer,CancellationToken ct)=>ReadSeal<PaymentDaySummary>("Payment",w,id,bearer,ct);
    private async Task<SourceSealRead<T>> ReadSeal<T>(string service,EndOfDayWindow w,Guid id,string bearer,CancellationToken ct)
    {
        string query=$"organizationId={w.OrganizationId:D}&restaurantId={w.RestaurantId:D}&branchId={w.BranchId:D}&fromUtc={Uri.EscapeDataString(w.FromUtc.ToString("O"))}&toUtc={Uri.EscapeDataString(w.ToUtc.ToString("O"))}";
        using var request=new HttpRequestMessage(HttpMethod.Get,$"api/{service.ToLowerInvariant()}/v1/customer/day-cutoffs/seals/{id:D}?{query}");
        request.Headers.Authorization=AuthenticationHeaderValue.Parse(bearer);
        using var response=await clients.CreateClient("EndOfDay"+service).SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct);
        if(response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)throw new UnauthorizedAccessException();
        response.EnsureSuccessStatusCode();
        return await NexaConnect.Infrastructure.Http.BoundedJson.ReadAsync<SourceSealRead<T>>(response,16*1024*1024,ct);
    }
}
