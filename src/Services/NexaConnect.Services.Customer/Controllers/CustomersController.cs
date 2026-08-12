using Microsoft.AspNetCore.Mvc;
using NexaConnect.Services.Customer.Application.Customers;
using NexaConnect.Services.Customer.Application.Tenant;
using NexaConnect.Contracts.Platform;
using NexaConnect.Infrastructure.Authorization;

namespace NexaConnect.Services.Customer.Controllers;

[ApiController]
[Route("api/customer/v1/organizations/{organizationId:guid}/customers")]
public sealed class CustomersController(ICustomers customers, ICustomerTenantAuthorizer tenantAuthorizer) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<CustomerProfile>> Create(
        Guid organizationId, CreateCustomerRequest request, CancellationToken cancellationToken)
    {
        if (!await HasCustomerAccessAsync(organizationId, ProductPermissions.CustomerProfileCreate, cancellationToken))
            return Forbid();
        try
        {
            CustomerProfile customer = customers.Create(new CreateCustomer(organizationId, request.CustomerNumber, request.DisplayName, request.IdentitySubjectId));
            return CreatedAtAction(nameof(Get), new { organizationId, id = customer.Id }, customer);
        }
        catch (ArgumentException exception) { return BadRequest(new { error = exception.Message }); }
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<CustomerProfile>> Get(Guid organizationId, Guid id, CancellationToken cancellationToken)
    {
        if (!await HasCustomerAccessAsync(organizationId, ProductPermissions.CustomerProfileRead, cancellationToken))
            return NotFound();
        CustomerProfile? customer = customers.Get(organizationId, id);
        return customer is null ? NotFound() : Ok(customer);
    }

    private async Task<bool> HasCustomerAccessAsync(
        Guid routeOrganizationId, string permission, CancellationToken cancellationToken)
    {
        if (ServiceWorkloadPrincipal.IsTrusted(User)) return true;
        return Guid.TryParse(Request.Headers[TenantContextHeaders.OrganizationId], out Guid contextOrganizationId)
            && contextOrganizationId == routeOrganizationId
            && string.Equals(Request.Headers[TenantContextHeaders.ApplicationCode], "nexa_connect", StringComparison.Ordinal)
            && Request.Headers.TryGetValue("Authorization", out var authorization)
            && await tenantAuthorizer.HasOrganizationAccessAsync(
                contextOrganizationId, permission, authorization.ToString(), cancellationToken);
    }
}

public sealed record CreateCustomerRequest(string CustomerNumber, string DisplayName, string? IdentitySubjectId);
