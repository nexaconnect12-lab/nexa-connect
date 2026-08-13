using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexaConnect.Contracts.Platform;
using NexaConnect.Services.Restaurant.Application.Branches;
using NexaConnect.Services.Restaurant.Application.Configuration;

namespace NexaConnect.Services.Restaurant.Controllers;

[ApiController, Authorize(Roles = "customer-owner,customer-admin")]
[Route("api/restaurant/v1/customer/organizations/{organizationId:guid}/configuration/branches/{branchId:guid}")]
public sealed class CustomerProductConfigurationController(BranchProductConfigurationService service, IBranchCustomerAuthorizer authorizer, ILogger<CustomerProductConfigurationController> logger) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(Guid organizationId, Guid branchId, CancellationToken cancellationToken)
    {
        if (!await Granted(organizationId, branchId, ProductPermissions.RestaurantConfigurationRead, cancellationToken)) return Forbid();
        BranchProductConfiguration? result = await service.GetAsync(organizationId, branchId, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPut]
    public async Task<IActionResult> Update(Guid organizationId, Guid branchId, UpdateBranchProductConfigurationCommand request, CancellationToken cancellationToken)
    {
        if (!await Granted(organizationId, branchId, ProductPermissions.RestaurantConfigurationManage, cancellationToken)) return Forbid();
        try
        {
            BranchProductConfiguration? result = await service.UpdateAsync(organizationId, branchId, request, Actor()!, cancellationToken);
            return result is null ? Conflict(new ProblemDetails { Title = "Branch was unavailable or changed concurrently.", Status = 409 }) : Ok(result);
        }
        catch (ArgumentException exception) { return BadRequest(new ProblemDetails { Title = exception.Message, Status = 400 }); }
    }

    private async Task<bool> Granted(Guid organizationId, Guid branchId, string permission, CancellationToken cancellationToken)
    {
        string? actor = Actor();
        bool granted = actor is not null && await authorizer.IsGrantedAsync(organizationId, null, branchId, permission, Request.Headers.Authorization.ToString(), cancellationToken);
        if (!granted) logger.LogWarning("Customer product configuration authorization denied for organization {OrganizationId}, branch {BranchId}, permission {Permission}, actor {ActorSubjectId}", organizationId, branchId, permission, actor);
        return granted;
    }

    private string? Actor() => User.FindFirstValue("sub") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
}
