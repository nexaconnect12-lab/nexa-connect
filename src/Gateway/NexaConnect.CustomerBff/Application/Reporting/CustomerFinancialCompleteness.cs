using System.Net;
using NexaConnect.Contracts.Platform;

namespace NexaConnect.CustomerBff.Application.Reporting;

public sealed record FinancialCompletenessRequest(Guid BranchId, DateTimeOffset FromUtc, DateTimeOffset ToUtc);

public interface ICustomerFinancialCompletenessPort
{
    Task<CurrentPlatformAccessResponse?> GetAccessAsync(string token, CancellationToken cancellationToken);
    Task<HttpResponseMessage> ReadAsync(TenantContext tenant, string token, FinancialCompletenessRequest request, CancellationToken cancellationToken);
}

public sealed class CustomerFinancialCompleteness(ICustomerFinancialCompletenessPort port)
{
    public async Task<HttpResponseMessage> ReadAsync(TenantContext tenant, string token, FinancialCompletenessRequest request, CancellationToken cancellationToken)
    {
        if (request.BranchId == Guid.Empty || request.FromUtc == default || request.ToUtc <= request.FromUtc
            || request.ToUtc - request.FromUtc > TimeSpan.FromDays(31) || request.ToUtc > DateTimeOffset.UtcNow)
            return new(HttpStatusCode.BadRequest);
        if (tenant.ApplicationCode != "nexa_connect") return new(HttpStatusCode.Forbidden);

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(20));
        var access = await port.GetAccessAsync(token, deadline.Token);
        if (access?.SubjectId != tenant.SubjectId || access.Organizations?.Any(x =>
                x.OrganizationId == tenant.OrganizationId && x.ApplicationCode == tenant.ApplicationCode) != true)
            return new(HttpStatusCode.Forbidden);
        return await port.ReadAsync(tenant, token, request, deadline.Token);
    }
}
