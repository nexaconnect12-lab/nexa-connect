using System.Text.Json;
using NexaConnect.Contracts.Platform;
using NexaConnect.Contracts.RestaurantQr;

namespace NexaConnect.CustomerBff.Application.Qr;

public enum QrAdminOperation { List, Create, Rotate, Revoke }
public sealed record QrGatewayReply(int Status, JsonElement? Body);
public interface IQrGatewayPort
{
    Task<CurrentPlatformAccessResponse?> AccessAsync(string bearer, CancellationToken ct);
    Task<QrGatewayReply> StaffAsync(TenantContext tenant, string bearer, Guid branch, QrAdminOperation operation, Guid? entry, object? body, CancellationToken ct);
    Task<QrGatewayReply> GuestAsync(string operation, string? session, QrEntryExchange? input, CancellationToken ct);
}
public sealed class QrGatewayService(IQrGatewayPort port)
{
    public async Task<QrGatewayReply> StaffAsync(TenantContext tenant, string bearer, Guid branch, QrAdminOperation operation, Guid? entry, object? body, CancellationToken ct)
    {
        var access = await port.AccessAsync(bearer, ct);
        if (branch == Guid.Empty || tenant.ApplicationCode != "nexa_connect" || access?.SubjectId != tenant.SubjectId
            || !access.Organizations.Any(o => o.OrganizationId == tenant.OrganizationId && o.ApplicationCode == tenant.ApplicationCode))
            return new(403, null);
        return await port.StaffAsync(tenant, bearer, branch, operation, entry, body, ct);
    }
}
