using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexaConnect.Contracts.Platform;
using NexaConnect.Infrastructure.Authentication;
using NexaConnect.Services.PlatformDirectory.Application.Support;

namespace NexaConnect.Services.PlatformDirectory.Controllers;

[ApiController]
[Route("api/platform-directory/v1/support-elevations")]
public sealed class SupportElevationsController(SupportElevationApplicationService elevations) : ControllerBase
{
    [HttpPost]
    [Authorize(Policy = NexaAuthorizationPolicies.PlatformSupport)]
    public async Task<ActionResult<SupportElevationSummary>> RequestAsync(
        RequestSupportElevationRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryActor(out string actor)) return Forbid();
        try
        {
            SupportElevationSummary result = await elevations.RequestAsync(request, actor, cancellationToken);
            return Created($"api/platform-directory/v1/support-elevations/{result.ElevationId:D}", result);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new ProblemDetails { Title = exception.Message, Status = StatusCodes.Status400BadRequest });
        }
    }

    [HttpGet("effective")]
    [Authorize(Policy = NexaAuthorizationPolicies.PlatformSupport)]
    public async Task<ActionResult<SupportElevationSummary>> GetEffectiveAsync(
        [FromQuery] Guid organizationId,
        [FromQuery] string applicationCode,
        CancellationToken cancellationToken)
    {
        if (!TryActor(out string actor)) return Forbid();
        try
        {
            SupportElevationSummary? result = await elevations.GetEffectiveAsync(
                organizationId, applicationCode, actor, cancellationToken);
            return result is null ? NotFound() : Ok(result);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new ProblemDetails { Title = exception.Message, Status = StatusCodes.Status400BadRequest });
        }
    }

    [HttpGet("{elevationId:guid}")]
    [Authorize(Policy = NexaAuthorizationPolicies.PlatformAuditReader)]
    public async Task<ActionResult<SupportElevationSummary>> GetAsync(Guid elevationId, CancellationToken cancellationToken)
    {
        try
        {
            SupportElevationSummary? result = await elevations.GetAsync(elevationId, cancellationToken);
            return result is null ? NotFound() : Ok(result);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new ProblemDetails { Title = exception.Message, Status = StatusCodes.Status400BadRequest });
        }
    }

    [HttpPost("{elevationId:guid}/approve")]
    [Authorize(Policy = NexaAuthorizationPolicies.PlatformAdministrator)]
    public async Task<ActionResult<SupportElevationSummary>> ApproveAsync(Guid elevationId, CancellationToken cancellationToken)
    {
        if (!TryActor(out string actor)) return Forbid();
        try
        {
            SupportElevationSummary? result = await elevations.ApproveAsync(elevationId, actor, cancellationToken);
            return result is null ? NotFound() : Ok(result);
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new ProblemDetails { Title = exception.Message, Status = StatusCodes.Status409Conflict });
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new ProblemDetails { Title = exception.Message, Status = StatusCodes.Status400BadRequest });
        }
        catch (SupportElevationConflictException exception)
        {
            return Conflict(new ProblemDetails { Title = exception.Message, Status = StatusCodes.Status409Conflict });
        }
    }

    [HttpPost("{elevationId:guid}/revoke")]
    [Authorize(Policy = NexaAuthorizationPolicies.PlatformAdministrator)]
    public async Task<ActionResult<SupportElevationSummary>> RevokeAsync(Guid elevationId, CancellationToken cancellationToken)
    {
        if (!TryActor(out string actor)) return Forbid();
        try
        {
            SupportElevationSummary? result = await elevations.RevokeAsync(elevationId, actor, cancellationToken);
            return result is null ? NotFound() : Ok(result);
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new ProblemDetails { Title = exception.Message, Status = StatusCodes.Status409Conflict });
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new ProblemDetails { Title = exception.Message, Status = StatusCodes.Status400BadRequest });
        }
        catch (SupportElevationConflictException exception)
        {
            return Conflict(new ProblemDetails { Title = exception.Message, Status = StatusCodes.Status409Conflict });
        }
    }

    private bool TryActor(out string actor)
    {
        actor = User.FindFirstValue(NexaAuthenticationDefaults.SubjectClaim)
            ?? User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? string.Empty;
        return !string.IsNullOrWhiteSpace(actor);
    }
}
