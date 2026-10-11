using System.Net;
using NexaConnect.Contracts.Platform;

namespace NexaConnect.CustomerBff.Application.Reporting;

public sealed record SettlementReportsRequest(Guid BranchId, DateOnly FromDate, DateOnly ToDate);

public interface ICustomerSettlementReportsPort
{
    Task<CurrentPlatformAccessResponse?> GetAccessAsync(string token, CancellationToken cancellationToken);
    Task<HttpResponseMessage> ReadAsync(TenantContext tenant, string token, SettlementReportsRequest request, CancellationToken cancellationToken);
}

public sealed class CustomerSettlementReports(ICustomerSettlementReportsPort port)
{
    public async Task<HttpResponseMessage> ReadAsync(TenantContext tenant, string token, SettlementReportsRequest request, CancellationToken cancellationToken)
    {
        if (request.BranchId == Guid.Empty || request.FromDate == default || request.ToDate <= request.FromDate
            || request.ToDate.DayNumber - request.FromDate.DayNumber > 31 || request.ToDate > DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1))
            return new(HttpStatusCode.BadRequest);
        if (tenant.ApplicationCode != "nexa_connect") return new(HttpStatusCode.Forbidden);

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        var access = await port.GetAccessAsync(token, deadline.Token);
        if (access?.SubjectId != tenant.SubjectId || access.Organizations?.Any(x =>
                x.OrganizationId == tenant.OrganizationId && x.ApplicationCode == tenant.ApplicationCode) != true)
            return new(HttpStatusCode.Forbidden);
        return await port.ReadAsync(tenant, token, request, deadline.Token);
    }
}
