using NexaConnect.Services.Restaurant.Application.Provisioning;

namespace NexaConnect.UnitTests;

public sealed class RestaurantProvisioningTests
{
    [Fact]
    public async Task Provisioning_normalizes_owned_reference_data()
    {
        var repository = new CapturingRepository();
        var service = new RestaurantProvisioningService(repository);
        Guid organizationId = Guid.NewGuid();

        RestaurantProvisioningResult result = await service.CreateRestaurantAsync(
            new(organizationId, "demo_store", " Demo Store ", "sgd", " Asia/Singapore "), " admin-sub ", default);

        Assert.Equal(organizationId, result.OrganizationId);
        Assert.Equal("SGD", repository.Restaurant!.Currency);
        Assert.Equal("Demo Store", repository.Restaurant.Name);
        Assert.Equal("admin-sub", repository.Actor);
    }

    [Theory]
    [InlineData("Bad Code", "SGD")]
    [InlineData("valid-code", "dollar")]
    public async Task Provisioning_rejects_invalid_codes_and_currencies(string code, string currency)
    {
        var service = new RestaurantProvisioningService(new CapturingRepository());
        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateRestaurantAsync(
            new(Guid.NewGuid(), code, "Demo", currency, "Asia/Singapore"), "admin", default));
    }

    [Fact]
    public async Task Platform_directory_queries_require_their_owner_identifiers()
    {
        var service = new RestaurantProvisioningService(new CapturingRepository());

        await Assert.ThrowsAsync<ArgumentException>(() => service.ListRestaurantsAsync(Guid.Empty, default));
        await Assert.ThrowsAsync<ArgumentException>(() => service.ListBranchesAsync(Guid.Empty, default));
    }

    private sealed class CapturingRepository : IRestaurantProvisioningRepository
    {
        public CreateRestaurantCommand? Restaurant { get; private set; }
        public string? Actor { get; private set; }
        public Task<RestaurantProvisioningResult> CreateRestaurantAsync(CreateRestaurantCommand command, string actor, CancellationToken cancellationToken)
        {
            Restaurant = command; Actor = actor;
            return Task.FromResult(new RestaurantProvisioningResult(Guid.NewGuid(), command.OrganizationId, command.Code, command.Name));
        }
        public Task<BranchProvisioningResult?> CreateBranchAsync(Guid restaurantId, CreateBranchCommand command, string actor, CancellationToken cancellationToken) =>
            Task.FromResult<BranchProvisioningResult?>(new(Guid.NewGuid(), restaurantId, Guid.NewGuid(), command.Code, command.Name));
        public Task<IReadOnlyCollection<PlatformRestaurantSummary>> ListRestaurantsAsync(Guid organizationId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<PlatformRestaurantSummary>>([]);
        public Task<IReadOnlyCollection<PlatformBranchSummary>> ListBranchesAsync(Guid restaurantId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<PlatformBranchSummary>>([]);
    }
}
