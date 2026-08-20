using Microsoft.AspNetCore.Mvc;
using NexaConnect.Services.Payment.Application.Intents;
using NexaConnect.Contracts.Platform;
using NexaConnect.Services.Payment.Application.Tenant;
using NexaConnect.Infrastructure.Authorization;
using System.Security.Claims;

namespace NexaConnect.Services.Payment.Controllers;

[ApiController]
[Route("api/payment/v1/intents")]
public sealed class PaymentIntentsController(IPaymentIntents intents, IPaymentTenantAuthorizer tenantAuthorizer,
    IPaymentAuthorizationService? authorizationService = null, IPaymentCaptureService? captureService = null) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<PaymentIntent>> Create(CreatePaymentIntent command, CancellationToken cancellationToken)
    {
        if (!TryGetOrganization(out Guid organizationId))
            return BadRequest(new { error = "A valid organization context is required." });
        if (!await HasCustomerAccessAsync(organizationId, command.RestaurantId, command.BranchId, command.OrderId,
            ProductPermissions.PaymentIntentCreate, cancellationToken)) return Forbid();
        try
        {
            string actor = ServiceWorkloadPrincipal.IsTrusted(User)
                ? User.FindFirstValue("azp") ?? "trusted-workload"
                : User.FindFirstValue("sub") ?? "customer-user";
            Guid correlationId = Guid.TryParse(HttpContext.TraceIdentifier, out Guid parsedCorrelationId) ? parsedCorrelationId : Guid.NewGuid();
            PaymentIntent intent = intents.Create(organizationId, command, new PaymentMutationContext(actor, correlationId));
            return CreatedAtAction(nameof(Get), new { id = intent.Id }, intent);
        }
        catch (ArgumentException exception) { return BadRequest(new { error = exception.Message }); }
        catch (PaymentIdempotencyConflictException exception) { return Conflict(new { error = exception.Message }); }
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PaymentIntent>> Get(Guid id, CancellationToken cancellationToken)
    {
        if (!TryGetOrganization(out Guid organizationId)) return NotFound();
        PaymentIntent? intent = intents.Get(organizationId, id);
        if (intent is null) return NotFound();
        return await HasCustomerAccessAsync(organizationId, intent.RestaurantId, intent.BranchId, intent.OrderId,
            ProductPermissions.PaymentIntentRead, cancellationToken) ? Ok(intent) : NotFound();
    }

    [HttpPost("{id:guid}/authorize")]
    public async Task<ActionResult<PaymentIntent>> Authorize(Guid id, CancellationToken cancellationToken)
    {
        if (!TryGetOrganization(out Guid organizationId)) return NotFound();
        if (!ServiceWorkloadPrincipal.IsTrusted(User)
            || !string.Equals(User.FindFirstValue("azp"), "nexaconnect-order-service", StringComparison.Ordinal))
            return Forbid();
        if (authorizationService is null) return Problem("Payment authorization is unavailable.", statusCode: 503);
        try
        {
            Guid correlationId = Guid.TryParse(HttpContext.TraceIdentifier, out Guid parsed) ? parsed : Guid.NewGuid();
            PaymentIntent? result = await authorizationService.AuthorizeAsync(organizationId, id,
                new PaymentMutationContext("nexaconnect-order-service", correlationId), cancellationToken);
            return result is null ? NotFound() : Ok(result);
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (PaymentConcurrencyException exception) { return Conflict(new { error = exception.Message }); }
        catch (InvalidOperationException exception) { return Conflict(new { error = exception.Message }); }
    }

    [HttpPost("{id:guid}/capture")]
    public async Task<ActionResult<PaymentIntent>> Capture(Guid id, CancellationToken cancellationToken)
    {
        if (!TryGetOrganization(out Guid organizationId)) return NotFound();
        if (!ServiceWorkloadPrincipal.IsTrusted(User)
            || !string.Equals(User.FindFirstValue("azp"), "nexaconnect-order-service", StringComparison.Ordinal)) return Forbid();
        if (captureService is null) return Problem("Payment capture is unavailable.", statusCode: 503);
        try
        {
            Guid correlationId = Guid.TryParse(HttpContext.TraceIdentifier, out Guid parsed) ? parsed : Guid.NewGuid();
            PaymentIntent? result = await captureService.CaptureAsync(organizationId, id,
                new PaymentMutationContext("nexaconnect-order-service", correlationId), cancellationToken);
            return result is null ? NotFound() : Ok(result);
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (PaymentConcurrencyException exception) { return Conflict(new { error = exception.Message }); }
        catch (InvalidOperationException exception) { return Conflict(new { error = exception.Message }); }
    }

    private async Task<bool> HasCustomerAccessAsync(Guid organizationId, Guid restaurantId, Guid branchId, Guid orderId, string permission,
        CancellationToken cancellationToken)
    {
        if (ServiceWorkloadPrincipal.IsTrusted(User))
            return string.Equals(User.FindFirstValue("azp"), "nexaconnect-order-service", StringComparison.Ordinal);
        return string.Equals(Request.Headers[TenantContextHeaders.ApplicationCode], "nexa_connect", StringComparison.Ordinal)
            && Request.Headers.TryGetValue("Authorization", out var authorization)
            && await tenantAuthorizer.CanAccessAsync(organizationId, restaurantId, branchId, orderId, permission,
                authorization.ToString(), cancellationToken);
    }

    private bool TryGetOrganization(out Guid organizationId) =>
        Guid.TryParse(Request.Headers[TenantContextHeaders.OrganizationId], out organizationId);
}
