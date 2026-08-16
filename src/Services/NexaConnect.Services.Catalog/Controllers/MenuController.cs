using Microsoft.AspNetCore.Mvc;
using NexaConnect.Contracts.Platform;
using NexaConnect.Services.Catalog.Application.Menu;
using NexaConnect.Services.Catalog.Application.Tenant;
using NexaConnect.Infrastructure.Authorization;
using System.Security.Claims;

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
            string actor = User.FindFirstValue("sub") ?? User.FindFirstValue("azp") ?? "trusted-workload";
            Guid correlationId = Guid.TryParse(HttpContext.TraceIdentifier, out Guid parsedCorrelationId) ? parsedCorrelationId : Guid.NewGuid();
            var context = new MenuMutationContext(actor, correlationId);
            MenuItem item = customerOrganizationId is Guid organization
                ? catalog.AddForOrganizationBranch(organization, branchId, command, context)
                : catalog.Add(branchId, command, context);
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
