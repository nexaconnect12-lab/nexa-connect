using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexaConnect.Contracts.Platform;
using NexaConnect.Services.PlatformDirectory.Application.CustomerMemberships;

namespace NexaConnect.Services.PlatformDirectory.Controllers;

[ApiController, Authorize(Roles="customer-owner,customer-admin")]
[Route("api/platform-directory/v1/customer/organizations/{organizationId:guid}/members")]
public sealed class CustomerMembershipsController(CustomerMembershipManagement management) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(Guid organizationId, CancellationToken cancellationToken) => Actor() is not { } actor ? Forbid() : await management.ListAsync(organizationId, actor, cancellationToken) is { } result ? Ok(result) : Forbid();

    [HttpPut("{subjectId}")]
    public async Task<IActionResult> Change(Guid organizationId, string subjectId, ChangeCustomerMembershipRequest request, CancellationToken cancellationToken)
    {
        if (Actor() is not { } actor) return Forbid();
        try { return await management.ChangeAsync(organizationId, subjectId, request, actor, cancellationToken) is { } result ? Ok(result) : Forbid(); }
        catch (CustomerMembershipConflictException exception) { return Conflict(new { error = exception.Message }); }
    }
    private string? Actor() => User.FindFirstValue("sub") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
}
