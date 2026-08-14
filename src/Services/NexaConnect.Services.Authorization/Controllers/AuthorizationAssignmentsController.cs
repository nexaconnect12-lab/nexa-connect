using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexaConnect.Infrastructure.Authentication;
using NexaConnect.Services.Authorization.Application.Assignments;

namespace NexaConnect.Services.Authorization.Controllers;

[ApiController]
[Route("api/authorization/v1/role-assignments")]
[Authorize(Policy = NexaAuthorizationPolicies.ProductRoleAdministrator)]
public sealed class AuthorizationAssignmentsController(IAuthorizationAssignmentService service) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<RoleAssignmentResult>> AssignAsync(AssignRoleRequest request, CancellationToken cancellationToken)
    {
        string assignedBy = User.FindFirst(NexaAuthenticationDefaults.SubjectClaim)?.Value
            ?? User.FindFirst(NexaAuthenticationDefaults.UsernameClaim)?.Value
            ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? throw new UnauthorizedAccessException();
        try
        {
            return Ok(await service.AssignAsync(new AssignRoleCommand(request.SubjectId, request.OrganizationId, request.RestaurantId, request.BranchId, request.RoleCode), assignedBy, cancellationToken));
        }
        catch (ArgumentException exception) { return BadRequest(new ProblemDetails { Title = exception.Message, Status = 400 }); }
        catch (InvalidOperationException exception) { return NotFound(new ProblemDetails { Title = exception.Message, Status = 404 }); }
    }
}

public sealed record AssignRoleRequest(string SubjectId, Guid OrganizationId, Guid? RestaurantId, Guid? BranchId, string RoleCode = "cashier");
