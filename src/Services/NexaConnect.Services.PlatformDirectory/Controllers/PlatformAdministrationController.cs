using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexaConnect.Contracts.Platform;
using NexaConnect.Infrastructure.Authentication;
using NexaConnect.Services.PlatformDirectory.Application.Administration;

namespace NexaConnect.Services.PlatformDirectory.Controllers;

[ApiController]
[Route("api/platform-directory/v1/platform")]
public sealed class PlatformAdministrationController(IPlatformAdministration administration) : ControllerBase
{
    [HttpGet("users"), Authorize(Policy = NexaAuthorizationPolicies.PlatformAdministrator)]
    public Task<IReadOnlyCollection<PlatformUserSummary>> ListUsersAsync(CancellationToken ct) => administration.ListUsersAsync(ct);

    [HttpPost("users"), Authorize(Policy = NexaAuthorizationPolicies.PlatformAdministrator)]
    public async Task<ActionResult<PlatformUserSummary>> CreateUserAsync(CreatePlatformUserRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await administration.CreateUserAsync(request, Actor(), ct));

    [HttpPatch("users/{subjectId}"), Authorize(Policy = NexaAuthorizationPolicies.PlatformAdministrator)]
    public async Task<ActionResult<PlatformUserSummary>> UpdateUserAsync(string subjectId, UpdatePlatformUserRequest request, CancellationToken ct) =>
        await administration.UpdateUserAsync(subjectId, request, Actor(), ct) is { } user ? Ok(user) : NotFound();

    [HttpPut("users/{subjectId}/roles"), Authorize(Policy = NexaAuthorizationPolicies.PlatformAdministrator)]
    public async Task<ActionResult<PlatformUserSummary>> ChangeRolesAsync(string subjectId, ChangePlatformUserRolesRequest request, CancellationToken ct) =>
        await administration.ChangeRolesAsync(subjectId, request, Actor(), ct) is { } user ? Ok(user) : NotFound();

    [HttpGet("roles"), Authorize(Policy = NexaAuthorizationPolicies.PlatformUser)]
    public IReadOnlyCollection<PlatformRoleSummary> ListRoles() => administration.ListRoles();

    [HttpGet("audit"), Authorize(Policy = NexaAuthorizationPolicies.PlatformAuditReader)]
    public Task<IReadOnlyCollection<PlatformAuditRecord>> QueryAuditAsync([FromQuery] PlatformAuditQuery query, CancellationToken ct) => administration.QueryAuditAsync(query, ct);

    [HttpGet("summary"), Authorize(Policy = NexaAuthorizationPolicies.PlatformUser)]
    public Task<PlatformSummary> GetSummaryAsync(CancellationToken ct) => administration.GetSummaryAsync(ct);

    private string Actor() => User.FindFirstValue(NexaAuthenticationDefaults.SubjectClaim) ?? User.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new UnauthorizedAccessException("Authenticated subject is missing.");
}
