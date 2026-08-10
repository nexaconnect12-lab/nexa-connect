using Microsoft.AspNetCore.Mvc;
using NexaConnect.Services.Order.Application.Orders;
using NexaConnect.Services.Order.Domain;
using NexaConnect.Services.Order.Application.Workflow;
using NexaConnect.Services.Order.Application.Tenant;
using NexaConnect.Contracts.Platform;

namespace NexaConnect.Services.Order.Controllers;

[ApiController]
[Route("api/order/v1/orders")]
public sealed class OrdersController(IOrderApplicationService orders) : ControllerBase
{
    [HttpPost]
    public ActionResult<OrderResponse> Create(CreateOrderRequest request)
    {
        try
        {
            OrderAggregate order = orders.Create(request);
            return CreatedAtAction(nameof(Get), new { orderId = order.Id }, ToResponse(order));
        }
        catch (ArgumentException exception) { return BadRequest(new { error = exception.Message }); }
    }

    [HttpGet("{orderId:guid}")]
    public ActionResult<OrderResponse> Get(Guid orderId)
    {
        OrderAggregate? order = orders.Get(orderId);
        return order is null ? NotFound() : Ok(ToResponse(order));
    }

    private static OrderResponse ToResponse(OrderAggregate order) =>
        new(order.Id, order.OrganizationId, order.BranchId, order.Status.ToString(), order.TotalAmount, order.Currency,
            order.Lines.Select(line => new OrderLineResponse(line.ProductId, line.Name, line.UnitPrice, line.Quantity, line.PreparationStation)).ToArray());
}

[ApiController]
[Route("api/order/v1/workflows")]
public sealed class OrderWorkflowController(PlaceOrderWorkflow workflow, IOrderTenantAuthorizer tenantAuthorizer) : ControllerBase
{
    [HttpPost("place")]
    public async Task<ActionResult<PlaceOrderResult>> Place(PlaceOrderRequest request, CancellationToken cancellationToken)
    {
        try
        {
            if (Request.Headers.TryGetValue(TenantContextHeaders.PortalRequest, out var portal)
                && string.Equals(portal.ToString(), "customer", StringComparison.Ordinal))
            {
                if (!Guid.TryParse(Request.Headers[TenantContextHeaders.OrganizationId], out Guid contextOrganization)
                    || contextOrganization != request.OrganizationId
                    || !string.Equals(Request.Headers[TenantContextHeaders.ApplicationCode], "nexa_connect", StringComparison.Ordinal)
                    || !Request.Headers.TryGetValue("Authorization", out var authorization)
                    || !await tenantAuthorizer.HasBranchAccessAsync(contextOrganization, request.BranchId, authorization.ToString(), cancellationToken))
                    return Forbid();
            }
            if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
                return BadRequest(new { error = "IdempotencyKey is required." });
            var result = await workflow.ExecuteAsync(new PlaceOrderCommand(request.OrganizationId, request.BranchId,
                request.Lines.Select(line => new PlaceOrderLine(line.ProductId, line.Quantity)).ToArray(), request.Currency,
                request.PaymentMethod, request.RestaurantId, request.IdempotencyKey, request.OrderId, request.CorrelationId), cancellationToken);
            return result.Status is OrderStatus.Rejected or OrderStatus.PaymentFailed ? Conflict(result) : Ok(result);
        }
        catch (ArgumentException exception) { return BadRequest(new { error = exception.Message }); }
        catch (InvalidOperationException exception) { return UnprocessableEntity(new { error = exception.Message }); }
    }
}

public sealed record PlaceOrderRequest(Guid RestaurantId, Guid OrganizationId, Guid BranchId, string Currency,
    string PaymentMethod, string IdempotencyKey, IReadOnlyCollection<PlaceOrderRequestLine> Lines,
    Guid? OrderId = null, Guid? CorrelationId = null);
public sealed record PlaceOrderRequestLine(Guid ProductId, int Quantity);

public sealed record OrderResponse(Guid OrderId, Guid OrganizationId, Guid BranchId, string Status, decimal TotalAmount,
    string Currency, IReadOnlyCollection<OrderLineResponse> Lines);
public sealed record OrderLineResponse(Guid ProductId, string Name, decimal UnitPrice, int Quantity, string PreparationStation);
