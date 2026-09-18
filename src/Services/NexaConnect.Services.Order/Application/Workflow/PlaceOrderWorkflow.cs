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
    Guid? RestaurantId = null,
    string? IdempotencyKey = null,
    Guid? OrderId = null,
    Guid? CorrelationId = null,
    [property: System.Text.Json.Serialization.JsonIgnore] string? CardToken = null)
{
    public override string ToString() => $"PlaceOrderCommand {{ OrderId = {OrderId} }}";
}

public sealed class CardCheckoutOptions
{
    public bool EnableOmiseTestCheckout { get; set; }
}

public sealed record CatalogMenuItem(
    Guid ProductId,
    string Name,
    decimal UnitPrice,
    string Currency,
    bool Available,
    string PreparationStation);

public sealed record InventoryReservationResult(bool Reserved, Guid? ReservationId, string? Reason);
public sealed record KitchenTicketResult(Guid TicketId);
public sealed record PaymentResult(bool Completed, Guid? PaymentId, string? Reason, string Outcome = "authorized");
public sealed record PlaceOrderResult(Guid OrderId, OrderStatus Status, decimal TotalAmount, string Currency, bool CardTokenRequired = false);

public interface IMenuCatalogPort
{
    Task<IReadOnlyDictionary<Guid, CatalogMenuItem>> GetItemsAsync(
        Guid branchId, IReadOnlyCollection<Guid> productIds, CancellationToken cancellationToken);
}

public interface IInventoryReservationPort
{
    Task<InventoryReservationResult> ReserveAsync(
        Guid organizationId, Guid orderId, Guid branchId, IReadOnlyCollection<OrderLine> lines, CancellationToken cancellationToken);
    Task ReleaseAsync(Guid organizationId, Guid orderId, Guid branchId, CancellationToken cancellationToken) => Task.CompletedTask;
}

public interface IKitchenPort
{
    Task<KitchenTicketResult> CreateTicketAsync(
        Guid organizationId,Guid restaurantId,Guid orderId, Guid branchId, IReadOnlyCollection<OrderLine> lines, CancellationToken cancellationToken);
    Task CancelTicketAsync(Guid organizationId,Guid orderId, Guid branchId, CancellationToken cancellationToken) => Task.CompletedTask;
}

public interface IPaymentPort
{
    Task<PaymentResult> AuthorizeAsync(
        Guid organizationId, Guid restaurantId, Guid branchId, Guid orderId, decimal amount, string currency, string method,
        string? cardToken, CancellationToken cancellationToken)
        => cardToken is null ? AuthorizeAsync(organizationId, restaurantId, branchId, orderId, amount, currency, method, cancellationToken)
            : throw new NotSupportedException("This payment port does not accept card tokens.");
    Task<PaymentResult> AuthorizeAsync(
        Guid organizationId, Guid restaurantId, Guid branchId, Guid orderId, decimal amount, string currency, string method,
        CancellationToken cancellationToken);
}

public interface IOrderRepository
{
    Task SaveAsync(OrderAggregate order, CancellationToken cancellationToken);
}

public interface ITransactionalOrderRepository
{
    Task SaveWithEventAsync(OrderAggregate order, IIntegrationEvent integrationEvent, CancellationToken cancellationToken);
}

public interface IIdempotentOrderRepository
{
    Task<OrderAggregate?> FindByIdempotencyKeyAsync(Guid restaurantId, string key, CancellationToken cancellationToken);
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
    TimeProvider? timeProvider = null,
    Microsoft.Extensions.Options.IOptions<CardCheckoutOptions>? cardCheckout = null)
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public async Task<PlaceOrderResult> ExecuteAsync(
        PlaceOrderCommand command,
        CancellationToken cancellationToken)
    {
        Validate(command);
        if (command.PaymentMethod == "card_omise_test" && cardCheckout?.Value.EnableOmiseTestCheckout != true)
            throw new ArgumentException("Omise test checkout is disabled.");
        if (orders is IIdempotentOrderRepository && string.IsNullOrWhiteSpace(command.IdempotencyKey))
            throw new ArgumentException("Idempotency key is required for durable order persistence.");
        if (orders is IIdempotentOrderRepository idempotent && command.RestaurantId is { } restaurantId && !string.IsNullOrWhiteSpace(command.IdempotencyKey))
        {
            var existing = await idempotent.FindByIdempotencyKeyAsync(restaurantId, command.IdempotencyKey, cancellationToken);
            if (existing is not null)
            {
                if (command.PaymentMethod == "card_omise_test" && (existing.OrganizationId != command.OrganizationId || existing.BranchId != command.BranchId
                    || existing.Id != command.OrderId || existing.Currency != command.Currency
                    || existing.WorkflowPaymentMethod != command.PaymentMethod
                    || !existing.Lines.OrderBy(line => line.ProductId).Select(line => (line.ProductId, line.Quantity))
                        .SequenceEqual(command.Lines.OrderBy(line => line.ProductId).Select(line => (line.ProductId, line.Quantity)))))
                    throw new ArgumentException("The original order differs from this checkout. Restore its original scope and contents.");
                if (command.PaymentMethod == "card_omise_test" && existing.Status is OrderStatus.KitchenAccepted or OrderStatus.PaymentPending)
                    return await CompletePaymentAsync(existing, command, existing.WorkflowCorrelationId ?? existing.Id, cancellationToken);
                return new PlaceOrderResult(existing.Id, existing.Status, existing.TotalAmount, existing.Currency);
            }
        }
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
        var order = OrderAggregate.Create(orderId, command.OrganizationId, command.BranchId, orderLines, command.Currency,
            command.RestaurantId, idempotencyKey: command.IdempotencyKey,
            workflowPaymentMethod: command.PaymentMethod, workflowCorrelationId: correlationId);
        order.Submit();
        await PersistAsync(order, new OrderSubmittedV1(
            Guid.NewGuid(), correlationId, clock.GetUtcNow(), order.Id, order.OrganizationId, order.BranchId,
            order.Lines.Select(ToSnapshot).ToArray(), order.TotalAmount, order.Currency), cancellationToken);

        InventoryReservationResult reservation = await inventory.ReserveAsync(
            order.OrganizationId, order.Id, order.BranchId, order.Lines, cancellationToken);
        if (!reservation.Reserved || reservation.ReservationId is null)
        {
            order.Reject();
            await PersistAsync(order, new InventoryReservationRejectedV1(
                Guid.NewGuid(), correlationId, clock.GetUtcNow(), order.Id,
                reservation.Reason ?? "Inventory could not be reserved."), cancellationToken);
            return new PlaceOrderResult(order.Id, order.Status, order.TotalAmount, order.Currency);
        }
        order.MarkInventoryReserved();
        await PersistAsync(order, new InventoryReservedV1(
            Guid.NewGuid(), correlationId, clock.GetUtcNow(), order.Id, reservation.ReservationId.Value), cancellationToken);

        KitchenTicketResult ticket = await kitchen.CreateTicketAsync(order.OrganizationId,order.RestaurantId,order.Id, order.BranchId, order.Lines, cancellationToken);
        order.MarkKitchenAccepted();
        await PersistAsync(order, new KitchenTicketCreatedV1(
            Guid.NewGuid(), correlationId, clock.GetUtcNow(), order.Id, ticket.TicketId,
            order.Lines.Select(ToSnapshot).ToArray()), cancellationToken);

        if (command.PaymentMethod is "cash_manual" or "promptpay_manual")
        {
            return new PlaceOrderResult(order.Id, order.Status, order.TotalAmount, order.Currency);
        }

        return await CompletePaymentAsync(order, command, correlationId, cancellationToken);
    }

    private async Task<PlaceOrderResult> CompletePaymentAsync(OrderAggregate order, PlaceOrderCommand command,
        Guid correlationId, CancellationToken cancellationToken)
    {
        PaymentResult paid = await payment.AuthorizeAsync(
            order.OrganizationId, order.RestaurantId,
            order.BranchId, order.Id, order.TotalAmount, order.Currency, command.PaymentMethod, command.CardToken, cancellationToken);
        if (!paid.Completed && OrderWorkflowRecoveryService.IsUncertain(paid.Outcome))
        {
            if (paid.PaymentId is null)
                throw new InvalidOperationException("An uncertain payment authorization must identify its payment intent.");
            if (order.Status == OrderStatus.PaymentPending)
            {
                order.RestorePaymentIntent(paid.PaymentId.Value);
                return new PlaceOrderResult(order.Id, order.Status, order.TotalAmount, order.Currency, paid.Outcome == "awaiting_token");
            }
            order.MarkPaymentPending(paid.PaymentId.Value);
            await PersistAsync(order, new PaymentAuthorizationUncertainV1(
                Guid.NewGuid(), correlationId, clock.GetUtcNow(), order.Id, paid.PaymentId,
                paid.Reason ?? "Payment authorization requires reconciliation."), cancellationToken);
            return new PlaceOrderResult(order.Id, order.Status, order.TotalAmount, order.Currency, paid.Outcome == "awaiting_token");
        }
        if (!paid.Completed || paid.PaymentId is null)
        {
            await inventory.ReleaseAsync(order.OrganizationId, order.Id, order.BranchId, cancellationToken);
            await kitchen.CancelTicketAsync(order.OrganizationId,order.Id, order.BranchId, cancellationToken);
            order.MarkPaymentFailed();
            await PersistAsync(order, new PaymentFailedV1(
                Guid.NewGuid(), correlationId, clock.GetUtcNow(), order.Id,
                paid.Reason ?? "Payment was not completed."), cancellationToken);
            return new PlaceOrderResult(order.Id, order.Status, order.TotalAmount, order.Currency);
        }
        order.MarkPaid(paid.PaymentId.Value);
        await PersistAsync(order, new PaymentCompletedV1(
            OrderPaymentEventIdentity.Completion(order.Id, command.PaymentMethod), correlationId, clock.GetUtcNow(), order.Id, paid.PaymentId.Value,
            order.TotalAmount, order.Currency, command.PaymentMethod), cancellationToken);
        return new PlaceOrderResult(order.Id, order.Status, order.TotalAmount, order.Currency);
    }


    private async Task PersistAsync(OrderAggregate order, IIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        if (orders is ITransactionalOrderRepository transactional)
            await transactional.SaveWithEventAsync(order, integrationEvent, cancellationToken);
        else
        {
            await orders.SaveAsync(order, cancellationToken);
            await events.PublishAsync(integrationEvent, cancellationToken);
        }
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
        if (command.CardToken is not null && (command.PaymentMethod != "card_omise_test"
            || !System.Text.RegularExpressions.Regex.IsMatch(command.CardToken, @"\Atokn_test_[a-z0-9]{10,64}\z")))
            throw new ArgumentException("Use a fresh Omise test card token only for Omise test checkout.");
        if (command.PaymentMethod == "card_omise_test" && command.Currency != "THB")
            throw new ArgumentException("Omise test checkout requires THB.");
        if (command.PaymentMethod == "card_omise_test" && (command.OrderId is null || command.OrderId == Guid.Empty
            || command.RestaurantId is null || command.RestaurantId == Guid.Empty || string.IsNullOrWhiteSpace(command.IdempotencyKey)))
            throw new ArgumentException("Omise test checkout requires the original Order, restaurant and idempotency identity.");
        if (command.PaymentMethod is "cash_manual" or "promptpay_manual"
            && !string.Equals(command.Currency, "THB", StringComparison.Ordinal))
            throw new ArgumentException("Manual tender checkout requires THB.");
        if (command.IdempotencyKey is { Length: > 200 })
            throw new ArgumentException("Idempotency key is too long.");
    }
}
