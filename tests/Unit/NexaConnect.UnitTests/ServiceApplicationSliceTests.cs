using NexaConnect.Services.Catalog.Application.Menu;
using NexaConnect.Services.Catalog.Infrastructure;
using NexaConnect.Services.Customer.Application.Customers;
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
        var command = new CreatePaymentIntent(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "checkout-1", 10m, "USD", "cash");

        PaymentIntent first = payments.Create(command);
        PaymentIntent second = payments.Create(command);

        Assert.Equal(first.Id, second.Id);
    }

    [Fact]
    public void Customer_lookup_cannot_cross_organization_boundary()
    {
        var customers = new InMemoryCustomers();
        Guid organization = Guid.NewGuid();
        CustomerProfile customer = customers.Create(new CreateCustomer(organization, "C-1", "Ada", null));

        Assert.NotNull(customers.Get(organization, customer.Id));
        Assert.Null(customers.Get(Guid.NewGuid(), customer.Id));
    }

    [Fact]
    public void Notification_is_queued_with_safe_normalized_channel()
    {
        var notifications = new InMemoryNotificationSender();

        NotificationMessage message = notifications.Send(new SendNotification(" Email ", "ada@example.test", "Welcome", "Hello"));

        Assert.Equal("email", message.Channel);
        Assert.Equal("queued", message.Status);
    }
}
