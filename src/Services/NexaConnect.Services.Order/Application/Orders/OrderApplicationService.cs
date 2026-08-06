using System.Collections.Concurrent;
using NexaConnect.Services.Order.Application.Workflow;
using NexaConnect.Services.Order.Domain;

namespace NexaConnect.Services.Order.Application.Orders;

public sealed record CreateOrderLine(Guid ProductId, string Name, decimal UnitPrice, int Quantity, string PreparationStation);
public sealed record CreateOrderRequest(Guid OrganizationId, Guid BranchId, string Currency, IReadOnlyCollection<CreateOrderLine> Lines, Guid? RestaurantId = null);

public interface IOrderApplicationService
{
    OrderAggregate Create(CreateOrderRequest request);
    OrderAggregate? Get(Guid orderId);
}

public sealed class InMemoryOrderApplicationService : IOrderApplicationService, IOrderRepository
{
    private readonly ConcurrentDictionary<Guid, OrderAggregate> orders = new();

    public OrderAggregate Create(CreateOrderRequest request)
    {
        if (request.Lines is null)
            throw new ArgumentException("At least one order line is required.");
        var order = OrderAggregate.Create(Guid.NewGuid(),
            request.OrganizationId, request.BranchId,
            request.Lines.Select(line => new OrderLine(line.ProductId, line.Name, line.UnitPrice, line.Quantity, line.PreparationStation)).ToArray(),
            request.Currency);
        order.Submit();
        orders[order.Id] = order;
        return order;
    }

    public OrderAggregate? Get(Guid orderId) => orders.GetValueOrDefault(orderId);

    public Task SaveAsync(OrderAggregate order, CancellationToken cancellationToken)
    {
        orders[order.Id] = order;
        return Task.CompletedTask;
    }
}

public sealed class PostgresOrderApplicationService(Infrastructure.Persistence.PostgresOrderRepository repository) : IOrderApplicationService
{
    public OrderAggregate Create(CreateOrderRequest request)
    {
        var order = OrderAggregate.Create(Guid.NewGuid(), request.OrganizationId, request.BranchId,
            request.Lines.Select(line => new OrderLine(line.ProductId, line.Name, line.UnitPrice, line.Quantity, line.PreparationStation)).ToArray(),
            request.Currency, request.RestaurantId);
        order.Submit();
        repository.SaveAsync(order, CancellationToken.None).GetAwaiter().GetResult();
        return order;
    }

    public OrderAggregate? Get(Guid orderId) => repository.GetAsync(orderId, CancellationToken.None).GetAwaiter().GetResult();
}
