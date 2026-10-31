using NexaConnect.Contracts.Catalog;
using NexaConnect.Contracts.Platform;
using NexaConnect.Services.Catalog.Application.Menu;
using NexaConnect.Services.Catalog.Application.Tenant;

namespace NexaConnect.Services.Catalog.Application.Availability;

public interface IMenuAvailabilityRepository
{
    Task<IReadOnlyList<MenuAvailabilityItem>> ReadAsync(Guid organization, Guid branch, CancellationToken ct);
    Task<MenuAvailabilityItem?> ChangeAsync(Guid organization, Guid branch, Guid product,
        UpdateMenuAvailability input, MenuMutationContext actor, CancellationToken ct);
}
public sealed class MenuAvailabilityService(IMenuAvailabilityRepository repository, ICatalogTenantAuthorizer access)
{
    private async Task Require(Guid org, Guid branch, string permission, string bearer, CancellationToken ct)
    {
        if (org == Guid.Empty || branch == Guid.Empty || !await access.HasBranchAccessAsync(org, branch, permission, bearer, ct))
            throw new UnauthorizedAccessException();
    }
    public async Task<BranchMenuAvailability> ReadAsync(Guid org, Guid branch, string bearer, CancellationToken ct)
    {
        await Require(org, branch, ProductPermissions.CatalogMenuRead, bearer, ct);
        var items = await repository.ReadAsync(org, branch, ct);
        if (items.Count > 200) throw new ArgumentException("Menu exceeds management bounds.");
        await Require(org, branch, ProductPermissions.CatalogMenuRead, bearer, ct);
        return new(org, branch, items);
    }
    public async Task<MenuAvailabilityItem?> ChangeAsync(Guid org, Guid branch, Guid product, UpdateMenuAvailability input,
        string bearer, MenuMutationContext actor, CancellationToken ct)
    {
        await Require(org, branch, ProductPermissions.CatalogMenuWrite, bearer, ct);
        if (product == Guid.Empty || input is null || input.Available is null || input.ExpectedVersion is null or < 0 || actor.CorrelationId == Guid.Empty
            || string.IsNullOrWhiteSpace(actor.ActorSubjectId) || actor.ActorSubjectId.Length > 256 || actor.ActorSubjectId.Any(char.IsControl))
            throw new ArgumentException("Invalid availability command.");
        await Require(org, branch, ProductPermissions.CatalogMenuWrite, bearer, ct);
        return await repository.ChangeAsync(org, branch, product, input, actor, ct);
    }
}
