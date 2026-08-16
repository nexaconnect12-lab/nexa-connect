using System.Text.Json;
using NexaConnect.Contracts.IntegrationEvents;

namespace NexaConnect.UnitTests;

public sealed class CatalogIntegrationEventTests
{
    [Fact]
    public void Menu_item_changed_contract_preserves_tenant_and_commercial_snapshot()
    {
        Guid organizationId = Guid.NewGuid(); Guid branchId = Guid.NewGuid(); Guid productId = Guid.NewGuid();
        var message = new CatalogMenuItemChangedV1(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow,
            organizationId, branchId, productId, "Burger", 12.50m, "USD", "grill", true);
        CatalogMenuItemChangedV1 restored = JsonSerializer.Deserialize<CatalogMenuItemChangedV1>(JsonSerializer.Serialize(message))!;
        Assert.Equal(organizationId, restored.OrganizationId); Assert.Equal(branchId, restored.BranchId);
        Assert.Equal(productId, restored.ProductId); Assert.Equal(12.50m, restored.UnitPrice); Assert.True(restored.Available);
    }

    [Fact]
    public void In_memory_catalog_accepts_mutation_context_without_claiming_durability()
    {
        var catalog = new NexaConnect.Services.Catalog.Infrastructure.InMemoryMenuCatalog();
        Guid organizationId = Guid.NewGuid(); Guid branchId = Guid.NewGuid(); Guid productId = Guid.NewGuid();
        var result = catalog.AddForOrganizationBranch(organizationId, branchId,
            new(productId, "Burger", 12.50m, "usd", "grill"), new("subject-1", Guid.NewGuid()));
        Assert.Equal("USD", result.Currency); Assert.Single(catalog.GetForOrganizationBranch(organizationId, branchId));
    }
}
