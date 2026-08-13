using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexaConnect.Contracts.Platform;
using NexaConnect.Infrastructure.Authentication;
using NexaConnect.Services.PlatformDirectory.Application.ControlPlane;

namespace NexaConnect.Services.PlatformDirectory.Controllers;

[ApiController]
[Authorize(Policy = NexaAuthorizationPolicies.PlatformAdministrator)]
[Route("api/platform-directory/v1")]
public sealed class PlatformControlPlaneController(IPlatformDirectoryManagement management) : ControllerBase
{
    [HttpGet("organizations")]
    public Task<IReadOnlyCollection<OrganizationSummary>> ListOrganizationsAsync(CancellationToken cancellationToken) =>
        management.ListOrganizationsAsync(cancellationToken);

    [HttpPost("organizations")]
    public async Task<ActionResult<OrganizationSummary>> CreateOrganizationAsync(CreateOrganizationRequest request, CancellationToken cancellationToken)
    {
        if (!TryActor(out string? actor)) return Forbid();
        try { return StatusCode(StatusCodes.Status201Created, await management.CreateOrganizationAsync(request, actor!, cancellationToken)); }
        catch (PlatformDirectoryConflictException) { return Conflict(); }
    }

    [HttpPatch("organizations/{organizationId:guid}")]
    public async Task<IActionResult> UpdateOrganizationAsync(Guid organizationId, UpdateOrganizationRequest request, CancellationToken cancellationToken)
    {
        if (!TryActor(out string? actor)) return Forbid();
        return await management.UpdateOrganizationAsync(organizationId, request, actor!, cancellationToken) ? NoContent() : NotFound();
    }

    [HttpPut("organizations/{organizationId:guid}/members/{subjectId}")]
    public async Task<IActionResult> ChangeMembershipAsync(Guid organizationId, string subjectId, ChangeOrganizationMembershipRequest request, CancellationToken cancellationToken)
    {
        if (!TryActor(out string? actor)) return Forbid();
        return await management.ChangeMembershipAsync(organizationId, subjectId, request, actor!, cancellationToken) ? NoContent() : NotFound();
    }

    [HttpPost("products")]
    public async Task<ActionResult<ProductRegistration>> RegisterProductAsync(RegisterProductRequest request, CancellationToken cancellationToken)
    {
        if (!TryActor(out string? actor)) return Forbid();
        try { return StatusCode(StatusCodes.Status201Created, await management.RegisterProductAsync(request, actor!, cancellationToken)); }
        catch (PlatformDirectoryConflictException) { return Conflict(); }
    }

    [HttpPut("organizations/{organizationId:guid}/products")]
    public async Task<IActionResult> ChangeProductAccessAsync(Guid organizationId, ChangeOrganizationProductAccessRequest request, CancellationToken cancellationToken)
    {
        if (!TryActor(out string? actor)) return Forbid();
        return await management.ChangeProductAccessAsync(organizationId, request, actor!, cancellationToken) ? NoContent() : NotFound();
    }

    private bool TryActor(out string? actor)
    {
        actor = User.FindFirstValue(NexaAuthenticationDefaults.SubjectClaim)
            ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        return !string.IsNullOrWhiteSpace(actor);
    }
}
