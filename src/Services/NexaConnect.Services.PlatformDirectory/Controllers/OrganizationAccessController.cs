using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexaConnect.Infrastructure.Authentication;

namespace NexaConnect.Services.PlatformDirectory.Controllers;

[ApiController]
[Route("api/platform-directory/v1/organizations")]
public sealed class OrganizationAccessController(OrganizationAccessStore accessStore) : ControllerBase
{
    [HttpGet("{organizationId:guid}/access")]
    public async Task<ActionResult<OrganizationAccessResponse>> GetAccessAsync(
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        string? subjectId = User.FindFirst(NexaAuthenticationDefaults.SubjectClaim)?.Value;
        if (string.IsNullOrWhiteSpace(subjectId))
        {
            return Forbid();
        }

        bool granted = await accessStore.HasNexaConnectAccessAsync(
            organizationId,
            subjectId,
            cancellationToken);
        return granted ? Ok(new OrganizationAccessResponse(organizationId, granted)) : Forbid();
    }

    [Authorize(Policy = NexaAuthorizationPolicies.SystemAdministrator)]
    [HttpGet("{organizationId:guid}/members/{subjectId}/access")]
    public async Task<ActionResult<OrganizationAccessResponse>> GetMemberAccessAsync(
        Guid organizationId,
        string subjectId,
        CancellationToken cancellationToken)
    {
        bool granted = await accessStore.HasNexaConnectAccessAsync(
            organizationId,
            subjectId,
            cancellationToken);
        return Ok(new OrganizationAccessResponse(organizationId, granted));
    }
}

public sealed record OrganizationAccessResponse(Guid OrganizationId, bool Granted);
