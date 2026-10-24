using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using NexaConnect.Contracts.Platform;
using NexaConnect.Infrastructure.Authentication;
using NexaConnect.Services.Payment.Application.Intents;
using NexaConnect.Services.Payment.Application.Refunds;
using NexaConnect.Services.Payment.Application.Tenant;

namespace NexaConnect.Services.Payment.Controllers;

[ApiController]
[Route("api/payment/v1/intents/{paymentIntentId:guid}/refunds")]
public sealed class PaymentRefundsController(IPaymentIntents intents, IPaymentRefunds refunds,
    IPaymentRefundService service, IPaymentTenantAuthorizer authorizer,
    ILogger<PaymentRefundsController> logger) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<PaymentRefundResponse>> Create(Guid paymentIntentId, RequestPaymentRefund request,
        CancellationToken cancellationToken)
    {
        if (!TryOrganization(out Guid organizationId)) return NotFound();
        PaymentIntent? intent = intents.Get(organizationId, paymentIntentId);
        if (intent is null) return NotFound();
        if (request.OperationId == Guid.Empty || request.Amount <= 0 || decimal.Round(request.Amount, 4) != request.Amount
            || request.Currency?.Trim().Length != 3 || request.ReasonCode?.Trim().ToLowerInvariant() is not
                ("customer_request" or "duplicate_charge" or "item_unavailable" or "service_issue" or "other"))
            return BadRequest(new { error = "A valid operation, amount, currency, and reason are required." });
        PaymentAccessDecision decision = await Decide(intent, ProductPermissions.PaymentRefundCreate, cancellationToken,
            request.Amount, request.Currency);
        if (!decision.Granted || decision.DecisionId == Guid.Empty
            || decision.EvaluatedLimit is { } limit && request.Amount > limit)
        {
            logger.LogWarning("Payment refund authorization boundary denied for intent {PaymentIntentId}.", paymentIntentId);
            return Forbid();
        }
        try
        {
            Guid correlationId = Guid.TryParse(HttpContext.TraceIdentifier, out Guid parsed) ? parsed : Guid.NewGuid();
            var command = new CreatePaymentRefund(request.OperationId, request.Amount, request.Currency,
                request.ReasonCode, decision.DecisionId);
            PaymentRefund refund = await service.RefundAsync(organizationId, paymentIntentId, command,
                new PaymentMutationContext(User.FindFirstValue("sub") ?? "customer-user", correlationId), cancellationToken);
            PaymentRefundResponse response = ToResponse(refund);
            return refund.Status == "completed" ? CreatedAtAction(nameof(Get), new { paymentIntentId, refundId = refund.Id }, response)
                : AcceptedAtAction(nameof(Get), new { paymentIntentId, refundId = refund.Id }, response);
        }
        catch (ArgumentException exception) { return BadRequest(new { error = exception.Message }); }
        catch (PaymentRefundConflictException exception) { return Conflict(new { error = exception.Message }); }
        catch (PaymentConcurrencyException exception) { return Conflict(new { error = exception.Message }); }
        catch (InvalidOperationException exception) { return Conflict(new { error = exception.Message }); }
    }

    [HttpGet("{refundId:guid}")]
    public async Task<ActionResult<PaymentRefundResponse>> Get(Guid paymentIntentId, Guid refundId, CancellationToken cancellationToken)
    {
        if (!TryOrganization(out Guid organizationId)) return NotFound();
        PaymentIntent? intent = intents.Get(organizationId, paymentIntentId);
        PaymentRefund? refund = refunds.Get(organizationId, refundId);
        if (intent is null || refund is null || refund.PaymentIntentId != paymentIntentId) return NotFound();
        return (await Decide(intent, ProductPermissions.PaymentRefundRead, cancellationToken)).Granted ? Ok(ToResponse(refund)) : NotFound();
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyCollection<PaymentRefundResponse>>> List(Guid paymentIntentId, CancellationToken cancellationToken)
    {
        if (!TryOrganization(out Guid organizationId)) return NotFound();
        PaymentIntent? intent = intents.Get(organizationId, paymentIntentId);
        if (intent is null || !(await Decide(intent, ProductPermissions.PaymentRefundRead, cancellationToken)).Granted) return NotFound();
        return Ok(refunds.List(organizationId, paymentIntentId).Select(ToResponse).ToArray());
    }

    [HttpGet("by-operation/{operationId:guid}")]
    public async Task<ActionResult<PaymentRefundResponse>> GetByOperation(Guid paymentIntentId, Guid operationId, CancellationToken cancellationToken)
    {
        if (!TryOrganization(out Guid organizationId)) return NotFound();
        PaymentIntent? intent = intents.Get(organizationId, paymentIntentId);
        PaymentRefund? refund = refunds.GetByOperation(organizationId, paymentIntentId, operationId);
        if (intent is null || refund is null) return NotFound();
        return (await Decide(intent, ProductPermissions.PaymentRefundRead, cancellationToken)).Granted ? Ok(ToResponse(refund)) : NotFound();
    }

    private async Task<PaymentAccessDecision> Decide(PaymentIntent intent, string permission, CancellationToken token,
        decimal? amount = null, string? currency = null)
    {
        if (!string.Equals(Request.Headers[TenantContextHeaders.ApplicationCode], "nexa_connect", StringComparison.Ordinal)
            || !Request.Headers.TryGetValue("Authorization", out var authorization)) return new(false, Guid.Empty);
        return await authorizer.DecideAsync(intent.OrganizationId, intent.RestaurantId, intent.BranchId, intent.OrderId,
            permission, authorization.ToString(), token, amount, currency);
    }

    private bool TryOrganization(out Guid id) => Guid.TryParse(Request.Headers[TenantContextHeaders.OrganizationId], out id);
    private static PaymentRefundResponse ToResponse(PaymentRefund value) => new(value.Id, value.OrganizationId,
        value.RestaurantId, value.BranchId, value.OrderId, value.PaymentIntentId, value.OperationId, value.Amount,
        value.Currency, value.ReasonCode, value.Status, value.RequestedAtUtc, value.CompletedAtUtc,
        value.FailureCode, value.Receipt);
}

public sealed record RequestPaymentRefund(Guid OperationId, decimal Amount, string Currency, string ReasonCode);
public sealed record PaymentRefundResponse(Guid Id, Guid OrganizationId, Guid RestaurantId, Guid BranchId,
    Guid OrderId, Guid PaymentIntentId, Guid OperationId, decimal Amount, string Currency, string ReasonCode,
    string Status, DateTimeOffset RequestedAtUtc, DateTimeOffset? CompletedAtUtc, string? FailureCode,
    PaymentRefundReceipt? Receipt);
