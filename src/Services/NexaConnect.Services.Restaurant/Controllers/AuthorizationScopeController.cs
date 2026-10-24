using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexaConnect.Infrastructure.Authentication;
using NexaConnect.Services.Restaurant.Application.Authorization;

namespace NexaConnect.Services.Restaurant.Controllers;

[ApiController]
[Route("api/restaurant/v1/branches")]
public sealed class AuthorizationScopeController(IAuthorizationScopeReader scopeReader) : ControllerBase
{
    [Authorize(Policy = NexaAuthorizationPolicies.BranchScopeReader)]
    [HttpGet("{branchId:guid}/business-calendar")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Calendar(Guid branchId, CancellationToken ct)
    {
        var scope = await scopeReader.GetAsync(branchId, ct);
        return scope is null ? NotFound() : Ok(scope);
    }
    [Authorize(Policy = NexaAuthorizationPolicies.BranchScopeReader)]
    [HttpGet("{branchId:guid}/authorization-scope")]
    public async Task<ActionResult<AuthorizationScopeResponse>> GetAsync(
        Guid branchId, CancellationToken cancellationToken)
    {
        AuthorizationScope? scope = await scopeReader.GetAsync(branchId, cancellationToken);
        return scope is null
            ? NotFound()
            : Ok(new AuthorizationScopeResponse(scope.OrganizationId, scope.RestaurantId, scope.BranchId));
    }
}

public sealed record AuthorizationScopeResponse(Guid OrganizationId, Guid RestaurantId, Guid BranchId);
