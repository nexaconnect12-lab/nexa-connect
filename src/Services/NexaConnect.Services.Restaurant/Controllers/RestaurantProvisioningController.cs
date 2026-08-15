using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexaConnect.Infrastructure.Authentication;
using NexaConnect.Services.Restaurant.Application.Provisioning;

namespace NexaConnect.Services.Restaurant.Controllers;

[ApiController]
[Authorize(Policy = NexaAuthorizationPolicies.PlatformAdministrator)]
[Route("api/restaurant/v1/restaurants")]
public sealed class RestaurantProvisioningController(IRestaurantProvisioning provisioning, ILogger<RestaurantProvisioningController> logger) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyCollection<PlatformRestaurantSummary>>> ListRestaurantsAsync([FromQuery] Guid organizationId, CancellationToken cancellationToken)
    {
        try { return Ok(await provisioning.ListRestaurantsAsync(organizationId, cancellationToken)); }
        catch (ArgumentException exception) { return BadRequest(new ProblemDetails { Title = exception.Message, Status = 400 }); }
    }

    [HttpGet("{restaurantId:guid}/branches")]
    public async Task<ActionResult<IReadOnlyCollection<PlatformBranchSummary>>> ListBranchesAsync(Guid restaurantId, CancellationToken cancellationToken)
    {
        try { return Ok(await provisioning.ListBranchesAsync(restaurantId, cancellationToken)); }
        catch (ArgumentException exception) { return BadRequest(new ProblemDetails { Title = exception.Message, Status = 400 }); }
    }

    [HttpPost]
    public async Task<ActionResult<RestaurantProvisioningResult>> CreateRestaurantAsync(CreateRestaurantCommand request, CancellationToken cancellationToken)
    {
        string actor = User.FindFirstValue("sub") ?? throw new UnauthorizedAccessException();
        try { var result = await provisioning.CreateRestaurantAsync(request, actor, cancellationToken); logger.LogInformation("Restaurant provisioned for organization {OrganizationId} as {RestaurantId}", result.OrganizationId, result.RestaurantId); return StatusCode(201, result); }
        catch (ArgumentException exception) { return BadRequest(new ProblemDetails { Title = exception.Message, Status = 400 }); }
    }

    [HttpPost("{restaurantId:guid}/branches")]
    public async Task<ActionResult<BranchProvisioningResult>> CreateBranchAsync(Guid restaurantId, CreateBranchCommand request, CancellationToken cancellationToken)
    {
        string actor = User.FindFirstValue("sub") ?? throw new UnauthorizedAccessException();
        try { var result = await provisioning.CreateBranchAsync(restaurantId, request, actor, cancellationToken); if (result is null) return NotFound(); logger.LogInformation("Branch provisioned for restaurant {RestaurantId} as {BranchId}", result.RestaurantId, result.BranchId); return StatusCode(201, result); }
        catch (ArgumentException exception) { return BadRequest(new ProblemDetails { Title = exception.Message, Status = 400 }); }
    }
}
