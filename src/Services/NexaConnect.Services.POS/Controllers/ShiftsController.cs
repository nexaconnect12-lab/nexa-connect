using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using NexaConnect.Infrastructure.Authentication;
using NexaConnect.Services.POS.Application.Shifts;
using NexaConnect.Services.POS.Domain.Shifts;

namespace NexaConnect.Services.POS.Controllers;

[ApiController]
[Route("api/pos/v1/shifts")]
public sealed class ShiftsController(
    ShiftApplicationService shifts,
    ILogger<ShiftsController> logger,
    IWebHostEnvironment environment) : ControllerBase
{
    [HttpPost("{shiftId:guid}/close")]
    public async Task<IActionResult> CloseAsync(Guid shiftId, CancellationToken cancellationToken)
    {
        PosUserContext? user = GetUserContext();
        if (user is null)
        {
            logger.LogWarning("POS shift close denied: missing authenticated subject or bearer token.");
            return Deny("missing-subject");
        }

        try
        {
            bool closed = await shifts.CloseAsync(shiftId, user, cancellationToken);
            return closed ? NoContent() : NotFound();
        }
        catch (ShiftAuthorizationException exception)
        {
            logger.LogWarning("POS shift close denied at {Stage} for subject {Subject}.", exception.Stage, user.Subject);
            return Deny(exception.Stage);
        }
        catch (ShiftConflictException)
        {
            return Conflict(new ProblemDetails
            {
                Title = "The shift changed before it could be closed.",
                Status = StatusCodes.Status409Conflict
            });
        }
        catch (ShiftDependencyException exception)
        {
            logger.LogError(exception, "POS shift close dependency {Dependency} is unavailable.", exception.Dependency);
            return Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "A required POS dependency is temporarily unavailable.");
        }
    }

    [HttpPost("open")]
    public async Task<IActionResult> OpenAsync(
        OpenShiftRequest request,
        CancellationToken cancellationToken)
    {
        PosUserContext? user = GetUserContext();
        if (user is null)
        {
            logger.LogWarning("POS shift open denied: missing authenticated subject or bearer token.");
            return Deny("missing-subject");
        }

        try
        {
            OpenShiftResult result = await shifts.OpenAsync(
                new OpenShiftCommand(request.BranchId, request.StoreId, request.TerminalId, request.ShiftNumber),
                user,
                cancellationToken);
            return Ok(new OpenShiftResponse(result.ShiftId, result.AuthorizationDecisionId));
        }
        catch (ShiftValidationException exception)
        {
            return BadRequest(new ProblemDetails
            {
                Title = exception.Message,
                Status = StatusCodes.Status400BadRequest
            });
        }
        catch (ShiftAuthorizationException exception)
        {
            logger.LogWarning("POS shift open denied at {Stage} for subject {Subject}.", exception.Stage, user.Subject);
            return Deny(exception.Stage);
        }
        catch (ShiftConflictException)
        {
            return Conflict(new ProblemDetails
            {
                Title = "The terminal already has an open shift or the shift number is in use.",
                Status = StatusCodes.Status409Conflict
            });
        }
        catch (ShiftDependencyException exception)
        {
            logger.LogError(exception, "POS shift open dependency {Dependency} is unavailable.", exception.Dependency);
            return Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "A required POS dependency is temporarily unavailable.");
        }
    }

    private PosUserContext? GetUserContext()
    {
        string? subject = User.FindFirst(NexaAuthenticationDefaults.SubjectClaim)?.Value
            ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        string authorization = Request.Headers.Authorization.ToString();
        const string bearerPrefix = "Bearer ";
        string? token = authorization.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase)
            ? authorization[bearerPrefix.Length..].Trim()
            : null;
        return string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(token)
            ? null
            : new PosUserContext(subject, token);
    }

    private IActionResult Deny(string stage) => environment.IsDevelopment()
        ? Problem(
            statusCode: StatusCodes.Status403Forbidden,
            title: "POS shift authorization denied",
            extensions: new Dictionary<string, object?> { ["stage"] = stage })
        : Forbid();
}

public sealed record OpenShiftRequest(Guid BranchId, Guid StoreId, Guid TerminalId, string ShiftNumber);
public sealed record OpenShiftResponse(Guid ShiftId, Guid AuthorizationDecisionId);
