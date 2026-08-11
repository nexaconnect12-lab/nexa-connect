using Microsoft.AspNetCore.Mvc;
using NexaConnect.Services.Payment.Application.Intents;
using NexaConnect.Contracts.Platform;
using NexaConnect.Services.Payment.Application.Tenant;

namespace NexaConnect.Services.Payment.Controllers;

[ApiController]
[Route("api/payment/v1/intents")]
public sealed class PaymentIntentsController(IPaymentIntents intents, IPaymentTenantAuthorizer tenantAuthorizer) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<PaymentIntent>> Create(CreatePaymentIntent command, CancellationToken cancellationToken)
    {
        if (!await HasCustomerAccessAsync(command.RestaurantId, command.BranchId, command.OrderId, cancellationToken)) return Forbid();
        try
        {
            PaymentIntent intent = intents.Create(command);
            return CreatedAtAction(nameof(Get), new { id = intent.Id }, intent);
        }
        catch (ArgumentException exception) { return BadRequest(new { error = exception.Message }); }
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PaymentIntent>> Get(Guid id, CancellationToken cancellationToken)
    {
        PaymentIntent? intent = intents.Get(id);
        if (intent is null) return NotFound();
        return await HasCustomerAccessAsync(intent.RestaurantId, intent.BranchId, intent.OrderId, cancellationToken)
            ? Ok(intent) : Forbid();
    }

    private async Task<bool> HasCustomerAccessAsync(Guid restaurantId, Guid branchId, Guid orderId,
        CancellationToken cancellationToken)
    {
        if (!Request.Headers.TryGetValue(TenantContextHeaders.PortalRequest, out var portal)
            || !string.Equals(portal.ToString(), "customer", StringComparison.Ordinal)) return true;
        return Guid.TryParse(Request.Headers[TenantContextHeaders.OrganizationId], out Guid organizationId)
            && string.Equals(Request.Headers[TenantContextHeaders.ApplicationCode], "nexa_connect", StringComparison.Ordinal)
            && Request.Headers.TryGetValue("Authorization", out var authorization)
            && await tenantAuthorizer.CanAccessAsync(organizationId, restaurantId, branchId, orderId,
                authorization.ToString(), cancellationToken);
    }
}
