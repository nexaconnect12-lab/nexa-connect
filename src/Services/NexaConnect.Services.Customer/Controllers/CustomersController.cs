using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using NexaConnect.Services.Customer.Application.Customers;
using NexaConnect.Contracts.Platform;
using NexaConnect.Services.Customer.Domain;

namespace NexaConnect.Services.Customer.Controllers;

[ApiController]
[Route("api/customer/v1/organizations/{organizationId:guid}/customers")]
public sealed class CustomersController(
    CustomerProfileService profiles,
    ILogger<CustomersController> logger) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<CustomerProfile>> Create(
        Guid organizationId, CreateCustomerRequest request, CancellationToken cancellationToken)
    {
        try
        {
            CustomerProfile customer = await profiles.CreateAsync(
                new CreateCustomer(organizationId, request.CustomerNumber, request.DisplayName, request.IdentitySubjectId),
                RequestContext(), cancellationToken);
            return CreatedAtAction(nameof(Get), new { organizationId, id = customer.Id }, customer);
        }
        catch (CustomerAccessDeniedException exception)
        {
            LogDenied(organizationId, exception.Permission);
            return Forbid();
        }
        catch (ArgumentException exception) { return BadRequest(new { error = exception.Message }); }
        catch (CustomerIdempotencyConflictException exception) { return Conflict(new { error = exception.Message }); }
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<CustomerProfile>> Get(Guid organizationId, Guid id, CancellationToken cancellationToken)
    {
        try
        {
            CustomerProfile? customer = await profiles.GetAsync(organizationId, id, RequestContext(),
                cancellationToken);
            return customer is null ? NotFound() : Ok(customer);
        }
        catch (CustomerAccessDeniedException exception)
        {
            LogDenied(organizationId, exception.Permission);
            return NotFound();
        }
    }

    private CustomerRequestContext RequestContext()
    {
        string requestCorrelationId = HttpContext.TraceIdentifier;
        Guid eventCorrelationId = Guid.TryParse(requestCorrelationId, out Guid parsed)
            ? parsed
            : new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(requestCorrelationId))[..16]);
        Guid.TryParse(Request.Headers[TenantContextHeaders.OrganizationId], out Guid contextOrganizationId);
        return new CustomerRequestContext(contextOrganizationId,
            Request.Headers[TenantContextHeaders.ApplicationCode].ToString(),
            Request.Headers.Authorization.ToString(), User.FindFirstValue("sub") ?? "", eventCorrelationId,
            requestCorrelationId);
    }

    private void LogDenied(Guid organizationId, string permission) =>
        logger.LogWarning("Customer authorization denied for organization {OrganizationId} and permission {Permission}.",
            organizationId, permission);
}

public sealed record CreateCustomerRequest(string CustomerNumber, string DisplayName, string? IdentitySubjectId);
