using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexaConnect.Infrastructure.Authentication;
using NexaConnect.Services.Restaurant.Application.Authorization;

namespace NexaConnect.Services.Restaurant.Controllers;

[ApiController]
[Route("api/restaurant/v1/branches")]
public sealed class AuthorizationScopeController(IAuthorizationScopeReader scopeReader) : ControllerBase
{
    [Authorize(Policy = NexaAuthorizationPolicies.ServiceWorkload)]
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
