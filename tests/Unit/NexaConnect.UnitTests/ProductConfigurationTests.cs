using NexaConnect.Services.Restaurant.Application.Configuration;

namespace NexaConnect.UnitTests;

public sealed class ProductConfigurationTests
{
    [Theory]
    [InlineData("nexaconnect-payment-service")]
    [InlineData("nexaconnect-pos")]
    [InlineData("")]
    public async Task Pricing_read_rejects_non_order_callers(string client)
    {
        var context = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        context.User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
            [new System.Security.Claims.Claim("azp", client)], "test"));
        context.Request.Headers["X-Nexa-Organization-Id"] = Guid.NewGuid().ToString();
        context.Request.Headers["X-Nexa-Application-Code"] = "nexa_connect";
        var controller = new NexaConnect.Services.Restaurant.Controllers.BranchPricingController(
            new BranchProductConfigurationService(new Repository()),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<NexaConnect.Services.Restaurant.Controllers.BranchPricingController>.Instance)
        { ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext { HttpContext = context } };
        Assert.IsType<Microsoft.AspNetCore.Mvc.ForbidResult>(await controller.Get(Guid.NewGuid(), default));
    }

    [Theory]
    [InlineData(-1)] [InlineData(101)] [InlineData(7.001)]
    public async Task Tax_rates_are_bounded_and_have_two_decimal_precision(decimal tax)
    {
        var service = new BranchProductConfigurationService(new Repository());
        await Assert.ThrowsAsync<ArgumentException>(() => service.UpdateAsync(Guid.NewGuid(), Guid.NewGuid(),
            new(true, true, false, 0, 1, tax, false), "actor", default));
    }

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
        public Task<BranchProductConfiguration?> GetAsync(Guid o, Guid b, CancellationToken c, bool activeOnly = false) => Task.FromResult<BranchProductConfiguration?>(null);
        public Task<BranchProductConfiguration?> UpdateAsync(Guid o, Guid b, UpdateBranchProductConfigurationCommand command, string actor, CancellationToken c) { Command = command; Actor = actor; return Task.FromResult<BranchProductConfiguration?>(null); }
    }
}
