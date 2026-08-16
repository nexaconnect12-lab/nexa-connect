using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using NexaConnect.Contracts.IntegrationEvents;
using NexaConnect.Contracts.Platform;
using NexaConnect.Services.Customer.Application.Customers;
using NexaConnect.Services.Customer.Application.Tenant;
using NexaConnect.Services.Customer.Controllers;
using NexaConnect.Services.Customer.Domain;
using NexaConnect.Services.Customer.Infrastructure;

namespace NexaConnect.UnitTests;

public sealed class CustomerProfileIntegrationTests
{
    [Fact]
    public async Task Matching_create_replays_and_conflicting_reuse_is_rejected()
    {
        var customers = new CustomerProfileService(new InMemoryCustomers(), new AllowAuthorizer());
        Guid organizationId = Guid.NewGuid();
        var context = new CustomerRequestContext(organizationId, "nexa_connect", "Bearer customer", "actor",
            Guid.NewGuid(), "customer-test-001");
        var command = new CreateCustomer(organizationId, " C-100 ", " Ada ", " subject-1 ");

        CustomerProfile created = await customers.CreateAsync(command, context, default);
        CustomerProfile replay = await customers.CreateAsync(command, context, default);

        Assert.Equal(created.Id, replay.Id);
        Assert.Equal("C-100", created.CustomerNumber);
        Assert.Equal("Ada", created.DisplayName);
        Assert.Equal(1, created.ConcurrencyVersion);
        await Assert.ThrowsAsync<CustomerIdempotencyConflictException>(() => customers.CreateAsync(
            command with { DisplayName = "Grace" }, context, default));
    }

    [Fact]
    public void Created_contract_round_trips_without_profile_pii()
    {
        var value = new CustomerProfileCreatedV1(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow,
            Guid.NewGuid(), Guid.NewGuid(), "active", 1, "customer-test-002");

        string json = JsonSerializer.Serialize(value);
        CustomerProfileCreatedV1 copy = JsonSerializer.Deserialize<CustomerProfileCreatedV1>(json)!;

        Assert.Equal(value.CustomerId, copy.CustomerId);
        Assert.Equal("customer-test-002", copy.RequestCorrelationId);
        Assert.DoesNotContain("displayName", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("identitySubject", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Trusted_workload_without_tenant_context_cannot_create_profile()
    {
        var controller = new CustomersController(
            new CustomerProfileService(new InMemoryCustomers(), new DenyAuthorizer()),
            NullLogger<CustomersController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim("azp", "nexaconnect-order-service"), new Claim("sub", "order-service")], "test"))
                }
            }
        };

        ActionResult<CustomerProfile> result = await controller.Create(Guid.NewGuid(),
            new CreateCustomerRequest("C-1", "Ada", null), default);

        Assert.IsType<ForbidResult>(result.Result);
    }

    private sealed class DenyAuthorizer : ICustomerTenantAuthorizer
    {
        public Task<bool> HasOrganizationAccessAsync(Guid organizationId, string permission,
            string authorizationHeader, CancellationToken cancellationToken) => Task.FromResult(false);
    }

    private sealed class AllowAuthorizer : ICustomerTenantAuthorizer
    {
        public Task<bool> HasOrganizationAccessAsync(Guid organizationId, string permission,
            string authorizationHeader, CancellationToken cancellationToken) => Task.FromResult(true);
    }
}
