using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexaConnect.Services.Catalog.Application.Menu;

namespace NexaConnect.Services.Catalog.Controllers;

[ApiController, Authorize]
[Route("api/catalog/v1/internal/organizations/{organizationId:guid}/products")]
public sealed class InternalCatalogProductsController(IMenuCatalog catalog, ILogger<InternalCatalogProductsController> logger) : ControllerBase
{
    [HttpGet("{productId:guid}/exists")]
    public async Task<IActionResult> Exists(Guid organizationId, Guid productId, CancellationToken cancellationToken)
    {
        if (!string.Equals(User.FindFirst("azp")?.Value, "nexaconnect-media-service", StringComparison.Ordinal))
        {
            logger.LogWarning("Catalog internal product lookup denied for organization {OrganizationId}", organizationId);
            return Forbid();
        }

        return await catalog.ProductExistsAsync(organizationId, productId, cancellationToken) ? Ok(new { exists = true }) : NotFound();
    }
}
