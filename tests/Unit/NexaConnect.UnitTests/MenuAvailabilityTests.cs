using NexaConnect.Contracts.Catalog;
using NexaConnect.Contracts.Platform;
using NexaConnect.Services.Catalog.Application.Availability;
using NexaConnect.Services.Catalog.Application.Tenant;
using NexaConnect.Services.Catalog.Domain.Availability;
using NexaConnect.Services.Catalog.Infrastructure;

namespace NexaConnect.UnitTests;

public sealed class MenuAvailabilityTests
{
    [Fact] public void Availability_versions_reject_stale_writes_and_exact_noops_do_not_advance()
    {
        var initial = new MenuAvailabilityState(true, 0);
        Assert.Same(initial, initial.Change(true, 0));
        var sold = initial.Change(false, 0); Assert.False(sold.Available); Assert.Equal(1, sold.Version);
        Assert.Throws<MenuAvailabilityConflictException>(() => sold.Change(true, 0));
        Assert.Same(sold, sold.Change(false, 1)); Assert.True(sold.Change(true, 1).Available);
        Assert.Throws<ArgumentException>(() => sold.Change(true, -1));
        Assert.Throws<MenuAvailabilityConflictException>(() => new MenuAvailabilityState(false, long.MaxValue).Change(true, long.MaxValue));
    }
    [Fact] public async Task Scoped_changes_preserve_price_and_metadata_edits_do_not_restore_sold_out()
    {
        var access = new Access(); var menu = new InMemoryMenuCatalog(); var repo = new InMemoryMenuAvailabilityRepository(menu);
        var app = new MenuAvailabilityService(repo, access); var product = Guid.NewGuid();
        menu.AddForOrganizationBranch(access.Org, access.Branch, new(product, "Meal", 107, "THB", "kitchen"));
        var actor = new NexaConnect.Services.Catalog.Application.Menu.MenuMutationContext("manager", Guid.NewGuid());
        var sold = await app.ChangeAsync(access.Org, access.Branch, product, new(false, 0), "Bearer", actor, default);
        Assert.False(sold!.Available); Assert.Equal(1, sold.Version);
        Assert.False(menu.GetForOrganizationBranch(access.Org, access.Branch).Single().Available);
        Assert.Empty(await repo.ReadAsync(Guid.NewGuid(), access.Branch, default));
        Assert.Null(await repo.ChangeAsync(access.Org, Guid.NewGuid(), product, new(true, 1), actor, default));
        var edited = menu.AddForOrganizationBranch(access.Org, access.Branch, new(product, "Meal renamed", 108, "THB", "bar"));
        Assert.False(edited.Available); Assert.Equal(108, edited.UnitPrice);
        Assert.Equal(1, (await app.ReadAsync(access.Org, access.Branch, "Bearer", default)).Items.Single().Version);
        await Assert.ThrowsAsync<MenuAvailabilityConflictException>(() => app.ChangeAsync(access.Org, access.Branch, product, new(true, 0), "Bearer", actor, default));
        foreach (var input in new[] { new UpdateMenuAvailability(null, 1), new UpdateMenuAvailability(true, null) })
            await Assert.ThrowsAsync<ArgumentException>(() => app.ChangeAsync(access.Org, access.Branch, product, input, "Bearer", actor, default));
        access.Granted = false;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => app.ReadAsync(access.Org, access.Branch, "Bearer", default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => app.ChangeAsync(access.Org, access.Branch, product, new(true, 1), "Bearer", actor, default));
        Assert.False(menu.GetForOrganizationBranch(access.Org, access.Branch).Single().Available);
    }
    private sealed class Access : ICatalogTenantAuthorizer
    {
        public Guid Org=Guid.NewGuid(), Branch=Guid.NewGuid(); public bool Granted=true;
        public Task<bool> HasAccessAsync(Guid org,string permission,string bearer,CancellationToken ct)=>Task.FromResult(false);
        public Task<bool> HasBranchAccessAsync(Guid org,Guid branch,string permission,string bearer,CancellationToken ct)
        { Assert.Contains(permission,new[]{ProductPermissions.CatalogMenuRead,ProductPermissions.CatalogMenuWrite}); return Task.FromResult(Granted&&org==Org&&branch==Branch); }
    }
}
