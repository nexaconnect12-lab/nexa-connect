using Microsoft.AspNetCore.Mvc;
using NexaConnect.Contracts.Platform;
using NexaConnect.Services.Catalog.Application.Menu;
using NexaConnect.Services.Catalog.Application.Tenant;
using NexaConnect.Infrastructure.Authorization;

namespace NexaConnect.Services.Catalog.Controllers;

[ApiController]
[Route("api/catalog/v1/branches/{branchId:guid}/menu-items")]
public sealed class MenuController(IMenuCatalog catalog, ICatalogTenantAuthorizer tenantAuthorizer) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyCollection<MenuItem>>> Get(Guid branchId)
    {
        Guid? customerOrganizationId = null;
        if (!ServiceWorkloadPrincipal.IsTrusted(User))
        {
            if (!Guid.TryParse(Request.Headers[TenantContextHeaders.OrganizationId], out Guid organizationId)
                || !string.Equals(Request.Headers[TenantContextHeaders.ApplicationCode], "nexa_connect", StringComparison.Ordinal))
                return BadRequest(new { error = "A valid customer tenant context is required." });
            if (!Request.Headers.TryGetValue("Authorization", out var authorization)
                || !await tenantAuthorizer.HasBranchAccessAsync(organizationId, branchId, ProductPermissions.CatalogMenuRead,
                    authorization.ToString(), HttpContext.RequestAborted))
                return Forbid();
            customerOrganizationId = organizationId;
        }

        return Ok(customerOrganizationId is Guid organization
            ? catalog.GetForOrganizationBranch(organization, branchId)
            : catalog.GetForBranch(branchId));
    }

    [HttpPost]
    public async Task<ActionResult<MenuItem>> Add(Guid branchId, CreateMenuItem command)
    {
        Guid? customerOrganizationId = null;
        if (!ServiceWorkloadPrincipal.IsTrusted(User))
        {
            if (!TryGetOrganization(out Guid organizationId))
                return BadRequest(new { error = "A valid customer tenant context is required." });
            if (!Request.Headers.TryGetValue("Authorization", out var authorization)
                || !await tenantAuthorizer.HasBranchAccessAsync(organizationId, branchId, ProductPermissions.CatalogMenuWrite,
                    authorization.ToString(), HttpContext.RequestAborted))
                return Forbid();
            customerOrganizationId = organizationId;
        }
        try
        {
            MenuItem item = customerOrganizationId is Guid organization
                ? catalog.AddForOrganizationBranch(organization, branchId, command)
                : catalog.Add(branchId, command);
            return CreatedAtAction(nameof(Get), new { branchId }, item);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }

    private bool TryGetOrganization(out Guid organizationId) =>
        Guid.TryParse(Request.Headers[TenantContextHeaders.OrganizationId], out organizationId)
        && string.Equals(Request.Headers[TenantContextHeaders.ApplicationCode], "nexa_connect", StringComparison.Ordinal);
}
