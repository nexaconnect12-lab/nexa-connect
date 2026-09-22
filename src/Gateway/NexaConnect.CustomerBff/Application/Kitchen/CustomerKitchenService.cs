using System.ComponentModel.DataAnnotations;
using NexaConnect.Contracts.Platform;

namespace NexaConnect.CustomerBff.Application.Kitchen;

public enum KitchenOperation { Queue, Detail, Transition }
public sealed record KitchenTransitionRequest(
    [Required, RegularExpression("^(InProgress|Ready|Completed)$")] string TargetStatus,
    [Range(1, long.MaxValue)] long ExpectedConcurrencyVersion);
public sealed record KitchenRequest(Guid BranchId, Guid? TicketId = null, string? Station = null,
    int Limit = 50, string? Cursor = null, KitchenTransitionRequest? Transition = null);

public interface ICustomerKitchenPort
{
    Task<CurrentPlatformAccessResponse?> GetAccessAsync(string token, CancellationToken ct);
    Task<HttpResponseMessage> SendAsync(TenantContext tenant, string token, KitchenOperation operation, KitchenRequest request, CancellationToken ct);
}

public sealed class CustomerKitchenService(ICustomerKitchenPort port)
{
    public async Task<HttpResponseMessage> ExecuteAsync(TenantContext tenant, string token,
        KitchenOperation operation, KitchenRequest request, CancellationToken ct)
    {
        var access = await port.GetAccessAsync(token, ct);
        if (tenant.ApplicationCode != "nexa_connect" || access?.SubjectId != tenant.SubjectId ||
            !access.Organizations.Any(x => x.OrganizationId == tenant.OrganizationId && x.ApplicationCode == tenant.ApplicationCode))
            return new(System.Net.HttpStatusCode.Forbidden);
        return await port.SendAsync(tenant, token, operation, request, ct);
    }
}
