using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexaConnect.Infrastructure.Authentication;
using NexaConnect.Contracts.Platform;
using NexaConnect.Services.PlatformDirectory.Application.Access;

namespace NexaConnect.Services.PlatformDirectory.Controllers;

[ApiController]
[Route("api/platform-directory/v1/organizations")]
public sealed class OrganizationAccessController(IOrganizationAccessReader accessReader) : ControllerBase
{
    [HttpGet("/api/platform-directory/v1/me/access")]
    public async Task<ActionResult<CurrentPlatformAccessResponse>> GetCurrentAccessAsync(
        CancellationToken cancellationToken)
    {
        string? subjectId = User.FindFirst(NexaAuthenticationDefaults.SubjectClaim)?.Value;
        if (string.IsNullOrWhiteSpace(subjectId))
        {
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
        string? subjectId = User.FindFirst(NexaAuthenticationDefaults.SubjectClaim)?.Value;
        if (string.IsNullOrWhiteSpace(subjectId))
        {
            return Forbid();
        }

        bool granted = await accessReader.HasNexaConnectAccessAsync(
            organizationId,
            subjectId,
            cancellationToken);
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
}

public sealed record OrganizationAccessResponse(Guid OrganizationId, bool Granted);
