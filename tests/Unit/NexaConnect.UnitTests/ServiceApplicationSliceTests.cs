using NexaConnect.Services.Catalog.Application.Menu;
using NexaConnect.Services.Catalog.Infrastructure;
using NexaConnect.Services.Customer.Application.Customers;
using NexaConnect.Services.Customer.Application.Tenant;
using NexaConnect.Services.Customer.Infrastructure;
using NexaConnect.Services.Inventory.Application.Reservations;
using NexaConnect.Services.Inventory.Infrastructure;
using NexaConnect.Services.Notification.Application.Messages;
using NexaConnect.Services.Notification.Infrastructure;
using NexaConnect.Services.Payment.Application.Intents;
using NexaConnect.Services.Payment.Infrastructure;

namespace NexaConnect.UnitTests;

public sealed class ServiceApplicationSliceTests
{
    [Fact]
    public void Catalog_menu_is_scoped_to_branch()
    {
        var catalog = new InMemoryMenuCatalog();
        Guid branch = Guid.NewGuid();
        catalog.Add(branch, new CreateMenuItem(Guid.NewGuid(), "Burger", 10m, "USD", "grill"));

        Assert.Single(catalog.GetForBranch(branch));
        Assert.Empty(catalog.GetForBranch(Guid.NewGuid()));
    }

    [Fact]
    public void Inventory_reservation_decrements_available_stock()
    {
        var inventory = new InMemoryInventoryReservations();
        Guid branch = Guid.NewGuid();
        Guid product = Guid.NewGuid();
        inventory.SetStock(branch, product, 5m);

        inventory.Reserve(new ReserveStock(Guid.NewGuid(), branch, [new ReservationLine(product, 2m)]));

        Assert.Equal(3m, inventory.GetStock(branch).Single().AvailableQuantity);
    }

    [Fact]
    public void Payment_intent_is_idempotent_for_restaurant_and_key()
    {
        var payments = new InMemoryPaymentIntents();
        Guid organizationId = Guid.NewGuid();
        var command = new CreatePaymentIntent(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "checkout-1", 10m, "USD", "cash");
        var context = new PaymentMutationContext("test-user", Guid.NewGuid());

        PaymentIntent first = payments.Create(organizationId, command, context);
        PaymentIntent second = payments.Create(organizationId, command, context);

        Assert.Equal(first.Id, second.Id);
    }

    [Fact]
    public void Payment_idempotency_key_rejects_a_different_request()
    {
        var payments = new InMemoryPaymentIntents();
        Guid organizationId = Guid.NewGuid();
        var command = new CreatePaymentIntent(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "checkout-1", 10m, "USD", "cash");
        var context = new PaymentMutationContext("test-user", Guid.NewGuid());
        payments.Create(organizationId, command, context);

        Assert.Throws<PaymentIdempotencyConflictException>(() =>
            payments.Create(organizationId, command with { Amount = 11m }, context));
    }

    [Theory]
    [InlineData(1.23456)]
    [InlineData(1000000000000000.0)]
    public void Payment_rejects_amounts_outside_database_precision(decimal amount)
    {
        var payments = new InMemoryPaymentIntents();
        var command = new CreatePaymentIntent(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "checkout-1", amount, "USD", "cash");

        Assert.Throws<ArgumentException>(() => payments.Create(Guid.NewGuid(), command,
            new PaymentMutationContext("test-user", Guid.NewGuid())));
    }

    [Fact]
    public void Payment_rejects_actor_outside_audit_contract()
    {
        var payments = new InMemoryPaymentIntents();
        var command = new CreatePaymentIntent(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "checkout-1", 10m, "USD", "cash");

        Assert.Throws<ArgumentException>(() => payments.Create(Guid.NewGuid(), command,
            new PaymentMutationContext("actor\nspoof", Guid.NewGuid())));
        Assert.Throws<ArgumentException>(() => payments.Create(Guid.NewGuid(), command,
            new PaymentMutationContext(new string('a', 201), Guid.NewGuid())));
    }

    [Fact]
    public async Task Customer_lookup_cannot_cross_organization_boundary()
    {
        var customers = new CustomerProfileService(new InMemoryCustomers(), new AllowCustomerTenantAuthorizer());
        Guid organization = Guid.NewGuid();
        CustomerProfile customer = await customers.CreateAsync(new CreateCustomer(organization, "C-1", "Ada", null),
            new CustomerRequestContext(organization, "nexa_connect", "Bearer customer", "test-user", Guid.NewGuid(),
                Guid.NewGuid().ToString("D")), default);

        Assert.NotNull(await customers.GetAsync(organization, customer.Id,
            new CustomerRequestContext(organization, "nexa_connect", "Bearer customer", "test-user", Guid.NewGuid(),
                Guid.NewGuid().ToString("D")), default));
        Guid otherOrganization = Guid.NewGuid();
        Assert.Null(await customers.GetAsync(otherOrganization, customer.Id,
            new CustomerRequestContext(otherOrganization, "nexa_connect", "Bearer customer", "test-user", Guid.NewGuid(),
                Guid.NewGuid().ToString("D")), default));
    }

    [Fact]
    public void Notification_is_queued_with_safe_normalized_channel()
    {
        var notifications = new InMemoryNotificationSender();

        NotificationMessage message = notifications.Send(new SendNotification(Guid.NewGuid(), " Email ", "ada@example.test", "Welcome", "Hello"), "test-user");

        Assert.Equal("email", message.Channel);
        Assert.Equal("queued", message.Status);
    }

    private sealed class AllowCustomerTenantAuthorizer : ICustomerTenantAuthorizer
    {
        public Task<bool> HasOrganizationAccessAsync(Guid organizationId, string permission,
            string authorizationHeader, CancellationToken cancellationToken) => Task.FromResult(true);
    }
}
