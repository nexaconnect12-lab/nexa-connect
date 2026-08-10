using Microsoft.AspNetCore.Mvc;
using NexaConnect.Contracts.Platform;
using NexaConnect.Services.Catalog.Application.Menu;
using NexaConnect.Services.Catalog.Application.Tenant;

namespace NexaConnect.Services.Catalog.Controllers;

[ApiController]
[Route("api/catalog/v1/branches/{branchId:guid}/menu-items")]
public sealed class MenuController(IMenuCatalog catalog, ICatalogTenantAuthorizer tenantAuthorizer) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyCollection<MenuItem>>> Get(Guid branchId)
    {
        if (Request.Headers.TryGetValue(TenantContextHeaders.PortalRequest, out var portal)
            && string.Equals(portal.ToString(), "customer", StringComparison.Ordinal))
        {
            if (!Guid.TryParse(Request.Headers[TenantContextHeaders.OrganizationId], out Guid organizationId)
                || !string.Equals(Request.Headers[TenantContextHeaders.ApplicationCode], "nexa_connect", StringComparison.Ordinal))
                return BadRequest(new { error = "A valid customer tenant context is required." });
            if (!Request.Headers.TryGetValue("Authorization", out var authorization)
                || !await tenantAuthorizer.HasBranchAccessAsync(organizationId, branchId, authorization.ToString(), HttpContext.RequestAborted))
                return Forbid();
        }

        return Ok(catalog.GetForBranch(branchId));
    }

    [HttpPost]
    public ActionResult<MenuItem> Add(Guid branchId, CreateMenuItem command)
    {
        try
        {
            MenuItem item = catalog.Add(branchId, command);
            return CreatedAtAction(nameof(Get), new { branchId }, item);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }
}
