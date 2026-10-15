using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using NexaConnect.Services.POS.Application.Shifts;
using NexaConnect.Services.POS.Application.Terminals;
using NexaConnect.Services.POS.Controllers;

namespace NexaConnect.UnitTests;

public sealed class ManualTenderAdmissionTests
{
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Requires_matching_organization_and_pos_eligibility(bool organizationMatches, bool eligible)
    {
        var request = new ManualTenderAdmissionRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "cashier", "cash", "THB");
        var store = new Store(eligible);
        var app = new ManualTenderAdmission(store, new ScopeReader(new(organizationMatches ? request.OrganizationId : Guid.NewGuid(), request.RestaurantId, request.BranchId)), TimeProvider.System);
        var result = await app.AdmitAsync(request, default);
        Assert.Equal(organizationMatches && eligible, result is not null);
        Assert.Equal(organizationMatches ? 1 : 0, store.Reads);
    }

    [Fact]
    public async Task Customer_cannot_invoke_the_workload_admission_endpoint()
    {
        var controller = new ManualTenderAdmissionController(null!, NullLogger<ManualTenderAdmissionController>.Instance)
        { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
        Assert.IsType<ForbidResult>(await controller.Admit(new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "cashier", "cash", "THB"), default));
    }

    private sealed class Store(bool eligible) : IManualTenderAdmissionStore
    {
        public int Reads;
        public Task<bool> IsEligibleAsync(ManualTenderAdmissionRequest request, DateTimeOffset at, CancellationToken ct)
        { Reads++; return Task.FromResult(eligible); }
    }
    private sealed class ScopeReader(RestaurantAuthorizationScope scope) : IRestaurantScopeReader
    { public Task<RestaurantAuthorizationScope> GetAsync(Guid branch, CancellationToken ct) => Task.FromResult(scope); }
}
