using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexaConnect.Services.Restaurant.Application.Branches;

namespace NexaConnect.Services.Restaurant.Controllers;

[ApiController, Authorize(Roles = "customer-owner,customer-admin,customer-user,customer-viewer")]
[Route("api/restaurant/v1/customer/organizations/{organizationId:guid}/operational-branches")]
public sealed class OperationalBranchesController(OperationalBranchDiscovery discovery, ILogger<OperationalBranchesController> logger) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(Guid organizationId, [FromQuery] string capability, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        try
        {
            var branches = await discovery.ListAsync(organizationId, capability, Request.Headers.Authorization.ToString(), cancellationToken);
            if (branches.Count == 0) logger.LogInformation("Operational branch discovery returned no authorized branches for organization {OrganizationId}", organizationId);
            return Ok(branches);
        }
        catch (ArgumentException) { return BadRequest(new ProblemDetails { Title = "Invalid discovery scope or capability.", Status = 400 }); }
        catch (HttpRequestException)
        {
            logger.LogWarning("Operational branch discovery dependency unavailable for organization {OrganizationId}", organizationId);
            return StatusCode(503, new ProblemDetails { Title = "Branch discovery unavailable.", Status = 503 });
        }
    }
}
