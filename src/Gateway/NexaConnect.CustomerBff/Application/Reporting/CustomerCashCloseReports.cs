using NexaConnect.Contracts.Platform;

namespace NexaConnect.CustomerBff.Application.Reporting;

public sealed record CashCloseRequest(Guid BranchId, Guid StoreId, DateTimeOffset FromUtc, DateTimeOffset ToUtc, int Limit = 50, string? Cursor = null);
public interface ICustomerCashClosePort
{
    Task<CurrentPlatformAccessResponse?> GetAccessAsync(string token, CancellationToken ct);
    Task<HttpResponseMessage> ReadAsync(TenantContext tenant, string token, CashCloseRequest request, CancellationToken ct);
}
public sealed class CustomerCashCloseReports(ICustomerCashClosePort port)
{
    public async Task<HttpResponseMessage> ReadAsync(TenantContext tenant, string token, CashCloseRequest request, CancellationToken ct)
    {
        if (request.BranchId == Guid.Empty || request.StoreId == Guid.Empty || request.FromUtc >= request.ToUtc ||
            request.ToUtc - request.FromUtc > TimeSpan.FromDays(31) || request.Limit is < 1 or > 100 || request.Cursor is { Length: > 64 })
            return new(System.Net.HttpStatusCode.BadRequest);
        var access = await port.GetAccessAsync(token, ct);
        if (tenant.ApplicationCode != "nexa_connect" || access?.SubjectId != tenant.SubjectId ||
            !access.Organizations.Any(x => x.OrganizationId == tenant.OrganizationId && x.ApplicationCode == tenant.ApplicationCode))
            return new(System.Net.HttpStatusCode.Forbidden);
        return await port.ReadAsync(tenant, token, request, ct);
    }
}
