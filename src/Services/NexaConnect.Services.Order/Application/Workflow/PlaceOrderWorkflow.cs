using NexaConnect.Contracts.IntegrationEvents;
using NexaConnect.Services.Order.Domain;

namespace NexaConnect.Services.Order.Application.Workflow;

public sealed record PlaceOrderLine(Guid ProductId, int Quantity);

public sealed record PlaceOrderCommand(
    Guid OrganizationId,
    Guid BranchId,
    IReadOnlyCollection<PlaceOrderLine> Lines,
    string Currency,
    string PaymentMethod,
    Guid? OrderId = null,
    Guid? CorrelationId = null);

public sealed record CatalogMenuItem(
    Guid ProductId,
    string Name,
    decimal UnitPrice,
    string Currency,
    bool Available,
    string PreparationStation);

public sealed record InventoryReservationResult(bool Reserved, Guid? ReservationId, string? Reason);
public sealed record KitchenTicketResult(Guid TicketId);
public sealed record PaymentResult(bool Completed, Guid? PaymentId, string? Reason);
public sealed record PlaceOrderResult(Guid OrderId, OrderStatus Status, decimal TotalAmount, string Currency);

public interface IMenuCatalogPort
{
    Task<IReadOnlyDictionary<Guid, CatalogMenuItem>> GetItemsAsync(
        Guid branchId, IReadOnlyCollection<Guid> productIds, CancellationToken cancellationToken);
}

public interface IInventoryReservationPort
{
    Task<InventoryReservationResult> ReserveAsync(
        Guid orderId, Guid branchId, IReadOnlyCollection<OrderLine> lines, CancellationToken cancellationToken);
}

public interface IKitchenPort
{
    Task<KitchenTicketResult> CreateTicketAsync(
        Guid orderId, Guid branchId, IReadOnlyCollection<OrderLine> lines, CancellationToken cancellationToken);
}

public interface IPaymentPort
{
    Task<PaymentResult> AuthorizeAsync(
        Guid orderId, decimal amount, string currency, string method, CancellationToken cancellationToken);
}

public interface IOrderRepository
{
    Task SaveAsync(OrderAggregate order, CancellationToken cancellationToken);
}

public interface IIntegrationEventPublisher
{
    Task PublishAsync(IIntegrationEvent integrationEvent, CancellationToken cancellationToken);
}

public sealed class PlaceOrderWorkflow(
    IMenuCatalogPort menuCatalog,
    IInventoryReservationPort inventory,
    IKitchenPort kitchen,
    IPaymentPort payment,
    IOrderRepository orders,
    IIntegrationEventPublisher events,
    TimeProvider? timeProvider = null)
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public async Task<PlaceOrderResult> ExecuteAsync(
        PlaceOrderCommand command,
        CancellationToken cancellationToken)
    {
        Validate(command);
        Guid orderId = command.OrderId ?? Guid.NewGuid();
        Guid correlationId = command.CorrelationId ?? orderId;
        IReadOnlyDictionary<Guid, CatalogMenuItem> catalog = await menuCatalog.GetItemsAsync(
            command.BranchId, command.Lines.Select(line => line.ProductId).Distinct().ToArray(), cancellationToken);
        if (catalog.Count != command.Lines.Select(line => line.ProductId).Distinct().Count())
            throw new InvalidOperationException("One or more products are not present in the branch menu.");

        var orderLines = command.Lines.Select(line =>
        {
            CatalogMenuItem item = catalog[line.ProductId];
            if (!item.Available) throw new InvalidOperationException($"Product {line.ProductId} is unavailable.");
            if (!string.Equals(item.Currency, command.Currency, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Menu prices use a different currency than the order.");
            return new OrderLine(item.ProductId, item.Name, item.UnitPrice, line.Quantity, item.PreparationStation);
        }).ToArray();
        var order = OrderAggregate.Create(orderId, command.OrganizationId, command.BranchId, orderLines, command.Currency);
        order.Submit();
        await orders.SaveAsync(order, cancellationToken);
        await events.PublishAsync(new OrderSubmittedV1(
            Guid.NewGuid(), correlationId, clock.GetUtcNow(), order.Id, order.OrganizationId, order.BranchId,
            order.Lines.Select(ToSnapshot).ToArray(), order.TotalAmount, order.Currency), cancellationToken);

        InventoryReservationResult reservation = await inventory.ReserveAsync(order.Id, order.BranchId, order.Lines, cancellationToken);
        if (!reservation.Reserved || reservation.ReservationId is null)
        {
            order.Reject();
            await orders.SaveAsync(order, cancellationToken);
            await events.PublishAsync(new InventoryReservationRejectedV1(
                Guid.NewGuid(), correlationId, clock.GetUtcNow(), order.Id,
                reservation.Reason ?? "Inventory could not be reserved."), cancellationToken);
            return new PlaceOrderResult(order.Id, order.Status, order.TotalAmount, order.Currency);
        }
        order.MarkInventoryReserved();
        await orders.SaveAsync(order, cancellationToken);
        await events.PublishAsync(new InventoryReservedV1(
            Guid.NewGuid(), correlationId, clock.GetUtcNow(), order.Id, reservation.ReservationId.Value), cancellationToken);

        KitchenTicketResult ticket = await kitchen.CreateTicketAsync(order.Id, order.BranchId, order.Lines, cancellationToken);
        order.MarkKitchenAccepted();
        await orders.SaveAsync(order, cancellationToken);
        await events.PublishAsync(new KitchenTicketCreatedV1(
            Guid.NewGuid(), correlationId, clock.GetUtcNow(), order.Id, ticket.TicketId,
            order.Lines.Select(ToSnapshot).ToArray()), cancellationToken);

        PaymentResult paid = await payment.AuthorizeAsync(
            order.Id, order.TotalAmount, order.Currency, command.PaymentMethod, cancellationToken);
        if (!paid.Completed || paid.PaymentId is null)
        {
            order.MarkPaymentFailed();
            await orders.SaveAsync(order, cancellationToken);
            await events.PublishAsync(new PaymentFailedV1(
                Guid.NewGuid(), correlationId, clock.GetUtcNow(), order.Id,
                paid.Reason ?? "Payment was not completed."), cancellationToken);
            return new PlaceOrderResult(order.Id, order.Status, order.TotalAmount, order.Currency);
        }
        order.MarkPaid();
        await orders.SaveAsync(order, cancellationToken);
        await events.PublishAsync(new PaymentCompletedV1(
            Guid.NewGuid(), correlationId, clock.GetUtcNow(), order.Id, paid.PaymentId.Value,
            order.TotalAmount, order.Currency, command.PaymentMethod), cancellationToken);
        return new PlaceOrderResult(order.Id, order.Status, order.TotalAmount, order.Currency);
    }

    private static OrderLineSnapshot ToSnapshot(OrderLine line) =>
        new(line.ProductId, line.Name, line.UnitPrice, line.Quantity, line.PreparationStation);

    private static void Validate(PlaceOrderCommand command)
    {
        if (command.OrganizationId == Guid.Empty || command.BranchId == Guid.Empty)
            throw new ArgumentException("Organization and branch are required.");
        if (command.Lines is null || command.Lines.Count == 0)
            throw new ArgumentException("At least one order line is required.");
        if (command.Lines.Any(line => line.ProductId == Guid.Empty || line.Quantity <= 0))
            throw new ArgumentException("Order lines must have a product and positive quantity.");
        if (string.IsNullOrWhiteSpace(command.PaymentMethod))
            throw new ArgumentException("Payment method is required.");
    }
}
