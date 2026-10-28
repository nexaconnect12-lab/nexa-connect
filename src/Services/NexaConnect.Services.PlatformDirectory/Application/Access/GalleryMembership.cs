using NexaConnect.Contracts.Media;

namespace NexaConnect.Services.PlatformDirectory.Application.Access;

public sealed class GalleryMembership(IOrganizationAccessReader reader)
{
    public async Task<GalleryAccess> CheckAsync(GalleryMembershipQuery query, CancellationToken ct)
    {
        if(query.OrganizationId==Guid.Empty || string.IsNullOrWhiteSpace(query.Subject)
            || query.Subject.Length>256 || query.Subject.Any(char.IsControl)) return new(false);
        return new(await reader.HasNexaConnectAccessAsync(query.OrganizationId,query.Subject,ct));
    }
}
