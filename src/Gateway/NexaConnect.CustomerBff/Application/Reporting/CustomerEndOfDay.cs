using System.Net;
using NexaConnect.Contracts.Platform;

namespace NexaConnect.CustomerBff.Application.Reporting;

public sealed record EndOfDayRequest(Guid BranchId, DateOnly BusinessDate);

public interface ICustomerEndOfDayPort
{
    Task<CurrentPlatformAccessResponse?> GetAccessAsync(string token, CancellationToken cancellationToken);
    Task<HttpResponseMessage> ReadAsync(TenantContext tenant, string token, EndOfDayRequest request, CancellationToken cancellationToken);
}

public sealed class CustomerEndOfDay(ICustomerEndOfDayPort port)
{
    public async Task<HttpResponseMessage> ReadAsync(TenantContext tenant, string token, EndOfDayRequest request, CancellationToken cancellationToken)
    {
        if (request.BranchId == Guid.Empty || request.BusinessDate == default || request.BusinessDate == DateOnly.MaxValue)
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

