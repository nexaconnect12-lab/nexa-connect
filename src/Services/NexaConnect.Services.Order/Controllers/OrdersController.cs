using Microsoft.AspNetCore.Mvc;
using NexaConnect.Services.Order.Application.Orders;
using NexaConnect.Services.Order.Domain;
using NexaConnect.Services.Order.Application.Workflow;
using NexaConnect.Services.Order.Application.Tenant;
using NexaConnect.Contracts.Platform;
using NexaConnect.Infrastructure.Authorization;
using NexaConnect.Services.Order.Application.ManualTenders;
using System.Security.Claims;
using NexaConnect.Services.Order.Application.Cancellations;

namespace NexaConnect.Services.Order.Controllers;

[ApiController]
[Route("api/order/v1/orders")]
public sealed class OrdersController(IOrderApplicationService orders, IOrderTenantAuthorizer tenantAuthorizer,
    ManualTenderApplicationService manualTenders, OrderCancellationApplicationService cancellations,
    ILogger<OrdersController> logger) : ControllerBase
{
    [HttpPost]
    public IActionResult Create(CreateOrderRequest request) => StatusCode(410,
        new { error = "Use the authoritative quote and place workflow. Client-priced order creation is retired." });

    [HttpGet("{orderId:guid}")]
    public async Task<ActionResult<OrderResponse>> Get(Guid orderId, CancellationToken cancellationToken)
    {
        OrderAggregate? order = orders.Get(orderId);
        if (order is null) return NotFound();
        return await HasCustomerAccessAsync(order.OrganizationId, order.BranchId, ProductPermissions.OrderRead, cancellationToken)
            ? Ok(ToResponse(order)) : NotFound();
    }

    [HttpPost("{orderId:guid}/manual-settlement")]
    public async Task<ActionResult<ManualTenderResult>> ConfirmManualSettlement(Guid orderId,
        ConfirmManualTenderRequest request, CancellationToken cancellationToken)
    {
        if (request.OrganizationId == Guid.Empty || request.BranchId == Guid.Empty || orderId == Guid.Empty) return BadRequest();
        if (!Guid.TryParse(Request.Headers[TenantContextHeaders.OrganizationId], out Guid contextOrganization)
            || contextOrganization != request.OrganizationId
            || !string.Equals(Request.Headers[TenantContextHeaders.ApplicationCode], "nexa_connect", StringComparison.Ordinal)
            || !Request.Headers.TryGetValue("Authorization", out var authorization)) return Forbid();
        Guid? decision = await tenantAuthorizer.GetBranchDecisionAsync(contextOrganization, request.BranchId,
            ProductPermissions.OrderManualPaymentConfirm, authorization.ToString(), cancellationToken);
        if (decision is null) return Forbid();
        string? actor = User.FindFirstValue("sub");
        if (string.IsNullOrWhiteSpace(actor)) return Forbid();
        try
        {
            ManualTenderResult? result = await manualTenders.ConfirmAsync(new(request.OrganizationId, request.BranchId,
                orderId, request.TerminalId, request.IdempotencyKey, request.Method, request.Amount, request.Currency,
                request.ReceiptConfirmed, request.BankReference, actor, decision.Value, request.CorrelationId ?? Guid.NewGuid()), cancellationToken);
            if (result is null) return NotFound();
            return result.Replayed ? Ok(result) : StatusCode(StatusCodes.Status201Created, result);
        }
        catch (ArgumentException exception) { return BadRequest(new { error = exception.Message }); }
        catch (InvalidOperationException exception) { return Conflict(new { error = exception.Message }); }
    }

    [HttpPost("{orderId:guid}/cancellations")]
    public async Task<ActionResult<OrderCancellationResult>> Cancel(Guid orderId, CancelOrderRequest request,
        CancellationToken cancellationToken)
    {
        if (orderId == Guid.Empty || request.OrganizationId == Guid.Empty || request.BranchId == Guid.Empty
            || request.OperationId == Guid.Empty) return BadRequest();
        OrderAggregate? order = orders.Get(orderId);
        if (order is null || order.OrganizationId != request.OrganizationId || order.BranchId != request.BranchId)
            return NotFound();
        if (!Guid.TryParse(Request.Headers[TenantContextHeaders.OrganizationId], out Guid contextOrganization)
            || contextOrganization != request.OrganizationId
            || !string.Equals(Request.Headers[TenantContextHeaders.ApplicationCode], "nexa_connect", StringComparison.Ordinal)
            || !Request.Headers.TryGetValue("Authorization", out var authorization)) return Forbid();
        Guid? decision = await tenantAuthorizer.GetBranchDecisionAsync(contextOrganization, request.BranchId,
            ProductPermissions.OrderCancel, authorization.ToString(), cancellationToken);
        string? actor = User.FindFirstValue("sub");
        if (decision is null || string.IsNullOrWhiteSpace(actor))
        {
            logger.LogWarning("Order cancellation authorization denied");
            return Forbid();
        }
        try
        {
            var result = await cancellations.RequestAsync(new(orderId, request.OrganizationId, request.BranchId,
                request.OperationId, request.Reason, actor, decision.Value, request.CorrelationId ?? request.OperationId),
                cancellationToken);
            if (result is null) return NotFound();
            return result.Status switch
            {
                "completed" => Ok(result),
                "blocked" => Conflict(result),
                _ => StatusCode(StatusCodes.Status202Accepted, result)
            };
        }
        catch (ArgumentException exception) { return BadRequest(new { error = exception.Message }); }
        catch (OrderCancellationConflictException exception) { return Conflict(new { error = exception.Message }); }
    }

    private async Task<bool> HasCustomerAccessAsync(Guid organizationId, Guid branchId, string permission, CancellationToken cancellationToken)
    {
        if (ServiceWorkloadPrincipal.IsTrusted(User)) return true;
        return Guid.TryParse(Request.Headers[TenantContextHeaders.OrganizationId], out Guid contextOrganization)
            && contextOrganization == organizationId
            && string.Equals(Request.Headers[TenantContextHeaders.ApplicationCode], "nexa_connect", StringComparison.Ordinal)
            && Request.Headers.TryGetValue("Authorization", out var authorization)
            && await tenantAuthorizer.HasBranchAccessAsync(contextOrganization, branchId, permission,
                authorization.ToString(), cancellationToken);
    }

    private static OrderResponse ToResponse(OrderAggregate order) =>
        new(order.Id, order.OrganizationId, order.BranchId, order.Status.ToString(), order.TotalAmount, order.Currency,
            order.Lines.Select(line => new OrderLineResponse(line.ProductId, line.Name, line.UnitPrice, line.Quantity, line.PreparationStation)).ToArray(), order.Pricing);
}

[ApiController]
[Route("api/order/v1/workflows")]
public sealed class OrderWorkflowController(PlaceOrderWorkflow workflow, IOrderTenantAuthorizer tenantAuthorizer,
    OrderPricingService pricing, ILogger<OrderWorkflowController> logger) : ControllerBase
{
    [HttpPost("quote")]
    [RequestSizeLimit(65536)]
    public async Task<IActionResult> Quote(PlaceOrderRequest request, CancellationToken cancellationToken)
    {
        if (!await Granted(request, cancellationToken))
        {
            logger.LogWarning("Order pricing authorization denied");
            return Forbid();
        }
        try
        {
            if (request.Lines is null || request.Lines.Any(line => line is null)) return BadRequest();
            return Ok(await pricing.QuoteAsync(new(request.OrganizationId, request.BranchId,
                request.Lines.Select(l => new PlaceOrderLine(l.ProductId, l.Quantity)).ToArray(),
                request.Currency, request.PaymentMethod, request.RestaurantId), cancellationToken));
        }
        catch (ArgumentException) { return BadRequest(new { error = "Invalid checkout scope, pricing or items." }); }
        catch (OverflowException) { return BadRequest(new { error = "Order amount exceeds the supported range." }); }
        catch (HttpRequestException)
        {
            logger.LogWarning("Order pricing dependency unavailable");
            return StatusCode(503, new { error = "Pricing is temporarily unavailable." });
        }
    }

    private async Task<bool> Granted(PlaceOrderRequest request, CancellationToken cancellationToken) =>
        ServiceWorkloadPrincipal.IsTrusted(User) ||
        (Guid.TryParse(Request.Headers[TenantContextHeaders.OrganizationId], out Guid organization)
        && organization == request.OrganizationId
        && Request.Headers[TenantContextHeaders.ApplicationCode] == "nexa_connect"
        && await tenantAuthorizer.HasBranchAccessAsync(organization, request.BranchId, ProductPermissions.OrderPlace,
            Request.Headers.Authorization.ToString(), cancellationToken));

    [HttpPost("place")]
    [RequestSizeLimit(65536)]
    public async Task<ActionResult<PlaceOrderResult>> Place(PlaceOrderRequest request, CancellationToken cancellationToken)
    {
        try
        {
            if (!ServiceWorkloadPrincipal.IsTrusted(User))
            {
                if (!Guid.TryParse(Request.Headers[TenantContextHeaders.OrganizationId], out Guid contextOrganization)
                    || contextOrganization != request.OrganizationId
                    || !string.Equals(Request.Headers[TenantContextHeaders.ApplicationCode], "nexa_connect", StringComparison.Ordinal)
                    || !Request.Headers.TryGetValue("Authorization", out var authorization)
                    || !await tenantAuthorizer.HasBranchAccessAsync(contextOrganization, request.BranchId, ProductPermissions.OrderPlace,
                        authorization.ToString(), cancellationToken))
                {
                    logger.LogWarning("Order placement authorization denied");
                    return Forbid();
                }
            }
            if (request.Lines is null || request.Lines.Any(line => line is null)) return BadRequest();
            if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
                return BadRequest(new { error = "IdempotencyKey is required." });
            var result = await workflow.ExecuteAsync(new PlaceOrderCommand(request.OrganizationId, request.BranchId,
                request.Lines.Select(line => new PlaceOrderLine(line.ProductId, line.Quantity)).ToArray(), request.Currency,
                request.PaymentMethod, request.RestaurantId, request.IdempotencyKey, request.OrderId, request.CorrelationId, request.CardToken, request.PricingFingerprint), cancellationToken);
            return result.Status is OrderStatus.Rejected or OrderStatus.PaymentFailed ? Conflict(result) : Ok(result);
        }
        catch (ArgumentException exception) { return BadRequest(new { error = exception.Message }); }
        catch (PricingChangedException)
        {
            logger.LogInformation("Order pricing requires reconfirmation");
            return Conflict(new { code = "pricing_changed", error = "Review a fresh quote before confirming this order." });
        }
        catch (HttpRequestException)
        {
            logger.LogWarning("Order checkout dependency unavailable");
            return StatusCode(503, new { error = "Checkout is temporarily unavailable. Verify the original order." });
        }
        catch (OverflowException) { return BadRequest(new { error = "Order amount exceeds the supported range." }); }
        catch (InvalidOperationException exception) { return UnprocessableEntity(new { error = exception.Message }); }
    }
}

public sealed record PlaceOrderRequest(Guid RestaurantId, Guid OrganizationId, Guid BranchId, string Currency,
    string PaymentMethod, string IdempotencyKey, IReadOnlyCollection<PlaceOrderRequestLine> Lines,
    Guid? OrderId = null, Guid? CorrelationId = null, string? CardToken = null, string? PricingFingerprint = null)
{
    public override string ToString() => $"PlaceOrderRequest {{ OrderId = {OrderId} }}";
}
public sealed record PlaceOrderRequestLine(Guid ProductId, int Quantity);

public sealed record OrderResponse(Guid OrderId, Guid OrganizationId, Guid BranchId, string Status, decimal TotalAmount,
    string Currency, IReadOnlyCollection<OrderLineResponse> Lines, OrderPricing? Pricing = null);
public sealed record OrderLineResponse(Guid ProductId, string Name, decimal UnitPrice, int Quantity, string PreparationStation);
public sealed record ConfirmManualTenderRequest(Guid OrganizationId, Guid BranchId, Guid TerminalId, Guid IdempotencyKey,
    string Method, decimal Amount, string Currency, bool ReceiptConfirmed, string? BankReference = null, Guid? CorrelationId = null);
public sealed record CancelOrderRequest(Guid OrganizationId, Guid BranchId, Guid OperationId, string Reason,
    Guid? CorrelationId = null);
