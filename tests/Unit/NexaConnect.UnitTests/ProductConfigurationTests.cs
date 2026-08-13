using NexaConnect.Services.Restaurant.Application.Configuration;

namespace NexaConnect.UnitTests;

public sealed class ProductConfigurationTests
{
    [Fact]
    public async Task Update_requires_a_service_mode_and_valid_charge()
    {
        var service = new BranchProductConfigurationService(new Repository());
        await Assert.ThrowsAsync<ArgumentException>(() => service.UpdateAsync(Guid.NewGuid(), Guid.NewGuid(), new(false, false, false, 0, 1), "actor", default));
        await Assert.ThrowsAsync<ArgumentException>(() => service.UpdateAsync(Guid.NewGuid(), Guid.NewGuid(), new(true, false, false, 101, 1), "actor", default));
    }

    [Fact]
    public async Task Update_forwards_valid_typed_configuration()
    {
        var repository = new Repository(); var service = new BranchProductConfigurationService(repository);
        await service.UpdateAsync(Guid.NewGuid(), Guid.NewGuid(), new(true, true, true, 10.5m, 3), " actor ", default);
        Assert.Equal(10.5m, repository.Command!.ServiceChargePercent); Assert.Equal("actor", repository.Actor);
    }

    private sealed class Repository : IBranchProductConfigurationRepository
    {
        public UpdateBranchProductConfigurationCommand? Command; public string? Actor;
        public Task<BranchProductConfiguration?> GetAsync(Guid o, Guid b, CancellationToken c) => Task.FromResult<BranchProductConfiguration?>(null);
        public Task<BranchProductConfiguration?> UpdateAsync(Guid o, Guid b, UpdateBranchProductConfigurationCommand command, string actor, CancellationToken c) { Command = command; Actor = actor; return Task.FromResult<BranchProductConfiguration?>(null); }
    }
}
