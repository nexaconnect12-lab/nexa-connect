using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexaConnect.Infrastructure.Authentication;
using NexaConnect.Contracts.Platform;
using NexaConnect.Services.PlatformDirectory.Application.Access;

namespace NexaConnect.Services.PlatformDirectory.Controllers;

[ApiController]
[Route("api/platform-directory/v1/organizations")]
public sealed class OrganizationAccessController(
    IOrganizationAccessReader accessReader,
    ILogger<OrganizationAccessController> logger) : ControllerBase
{
    [HttpGet("/api/platform-directory/v1/me/access")]
    public async Task<ActionResult<CurrentPlatformAccessResponse>> GetCurrentAccessAsync(
        CancellationToken cancellationToken)
    {
        string? subjectId = AuthenticatedSubject();
        if (string.IsNullOrWhiteSpace(subjectId))
        {
            logger.LogWarning(
                "Current organization access denied because the authenticated token has no stable subject claim.");
            return Forbid();
        }

        IReadOnlyList<OrganizationApplicationAccess> organizations =
            await accessReader.GetCurrentAccessAsync(subjectId, cancellationToken);
        return Ok(new CurrentPlatformAccessResponse(subjectId, organizations));
    }

    [HttpGet("{organizationId:guid}/access")]
    public async Task<ActionResult<OrganizationAccessResponse>> GetAccessAsync(
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        string? subjectId = AuthenticatedSubject();
        if (string.IsNullOrWhiteSpace(subjectId))
        {
            logger.LogWarning(
                "Organization access denied for organization {OrganizationId} because the authenticated token has no stable subject claim.",
                organizationId);
            return Forbid();
        }

        bool granted = await accessReader.HasNexaConnectAccessAsync(
            organizationId,
            subjectId,
            cancellationToken);
        if (!granted)
        {
            logger.LogWarning(
                "Organization access denied for organization {OrganizationId}; reason {DenialReason}.",
                organizationId,
                "membership-or-application-access");
        }
        return granted ? Ok(new OrganizationAccessResponse(organizationId, granted)) : Forbid();
    }

    [Authorize(Policy = NexaAuthorizationPolicies.PlatformAdministrator)]
    [HttpGet("{organizationId:guid}/members/{subjectId}/access")]
    public async Task<ActionResult<OrganizationAccessResponse>> GetMemberAccessAsync(
        Guid organizationId,
        string subjectId,
        CancellationToken cancellationToken)
    {
        bool granted = await accessReader.HasNexaConnectAccessAsync(
            organizationId,
            subjectId,
            cancellationToken);
        return Ok(new OrganizationAccessResponse(organizationId, granted));
    }

    private string? AuthenticatedSubject() =>
        User.FindFirstValue(NexaAuthenticationDefaults.SubjectClaim)
        ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
}

public sealed record OrganizationAccessResponse(Guid OrganizationId, bool Granted);
