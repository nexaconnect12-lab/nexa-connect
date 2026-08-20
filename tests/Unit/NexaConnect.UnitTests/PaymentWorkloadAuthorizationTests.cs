using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NexaConnect.Contracts.Platform;
using NexaConnect.Services.Payment.Application.Intents;
using NexaConnect.Services.Payment.Application.Tenant;
using NexaConnect.Services.Payment.Controllers;

namespace NexaConnect.UnitTests;

public sealed class PaymentWorkloadAuthorizationTests
{
    [Fact]
    public async Task Non_order_workload_cannot_create_payment_intent()
    {
        var store = new RecordingStore();
        var controller = CreateController(store, "nexaconnect-catalog-service");

        ActionResult<PaymentIntent> result = await controller.Create(Command(), CancellationToken.None);

        Assert.IsType<ForbidResult>(result.Result);
        Assert.Equal(0, store.CreateCount);
    }

    [Fact]
    public async Task Order_workload_must_supply_organization_and_can_create()
    {
        var store = new RecordingStore();
        var controller = CreateController(store, "nexaconnect-order-service", includeOrganization: false);
        Assert.IsType<BadRequestObjectResult>((await controller.Create(Command(), CancellationToken.None)).Result);

        controller = CreateController(store, "nexaconnect-order-service");
        Assert.IsType<CreatedAtActionResult>((await controller.Create(Command(), CancellationToken.None)).Result);
        Assert.Equal(1, store.CreateCount);
    }

    private static PaymentIntentsController CreateController(RecordingStore store, string clientId, bool includeOrganization = true)
    {
        var context = new DefaultHttpContext();
        context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("azp", clientId)], "test"));
        if (includeOrganization) context.Request.Headers[TenantContextHeaders.OrganizationId] = Guid.NewGuid().ToString("D");
        return new PaymentIntentsController(store, new DenyTenantAuthorizer())
        {
            ControllerContext = new ControllerContext { HttpContext = context }
        };
    }

    private static CreatePaymentIntent Command() =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "order-key", 10m, "USD", "cash");

    private sealed class RecordingStore : IPaymentIntents
    {
        public int CreateCount { get; private set; }
        public PaymentIntent Create(Guid organizationId, CreatePaymentIntent command, PaymentMutationContext context)
        {
            CreateCount++;
            return new(Guid.NewGuid(), organizationId, command.RestaurantId, command.BranchId, command.OrderId,
                command.Amount, command.Currency, command.PaymentMethod, "pending", DateTimeOffset.UtcNow);
        }
        public PaymentIntent? Get(Guid organizationId, Guid id) => null;
        public PaymentAuthorizationLease BeginAuthorization(Guid organizationId, Guid id, PaymentMutationContext context) =>
            throw new NotSupportedException();
        public PaymentIntent CompleteAuthorization(Guid organizationId, Guid id, long expectedVersion, bool succeeded,
            string? providerAuthorizationId, string? failureCode, PaymentMutationContext context) => throw new NotSupportedException();
    }

    private sealed class DenyTenantAuthorizer : IPaymentTenantAuthorizer
    {
        public Task<bool> CanAccessAsync(Guid organizationId, Guid restaurantId, Guid branchId, Guid orderId,
            string permission, string bearerToken, CancellationToken cancellationToken) => Task.FromResult(false);
    }
}
