using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using NexaConnect.Infrastructure.Authentication;
using NexaConnect.Services.POS.Application.Shifts;
using NexaConnect.Services.POS.Application.Terminals;

namespace NexaConnect.Services.POS.Controllers;

[ApiController]
[Route("api/pos/v1/terminals")]
public sealed class TerminalsController(
    TerminalEnrollmentApplicationService terminals,
    ILogger<TerminalsController> logger) : ControllerBase
{
    [HttpPost("enroll")]
    public async Task<IActionResult> EnrollAsync(
        EnrollTerminalRequest request,
        CancellationToken cancellationToken)
    {
        string? subject = User.FindFirst(NexaAuthenticationDefaults.SubjectClaim)?.Value
            ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (User.Identity?.IsAuthenticated != true || string.IsNullOrWhiteSpace(subject))
        {
            return Unauthorized();
        }

        string authorizationHeader = Request.Headers.Authorization.ToString();
        if (!authorizationHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return Unauthorized();
        try
        {
            bool enrolled = await terminals.EnrollAsync(
                new EnrollTerminalCommand(
                    request.BranchId,
                    request.StoreId,
                    request.TerminalId,
                    request.Code,
                    request.DeviceType),
                new PosUserContext(subject, authorizationHeader[7..].Trim()),
                cancellationToken);
            return enrolled
                ? Created($"api/pos/v1/terminals/{request.TerminalId:D}", new { request.TerminalId })
                : NotFound();
        }
        catch (TerminalEnrollmentValidationException exception)
        {
            return BadRequest(new ProblemDetails { Title = exception.Message, Status = StatusCodes.Status400BadRequest });
        }
        catch (TerminalEnrollmentAuthorizationException exception)
        {
            logger.LogWarning(
                "POS terminal enrollment denied at {Stage} for subject {Subject}.",
                exception.Stage,
                subject);
            return Forbid();
        }
        catch (TerminalEnrollmentDependencyException exception)
        {
            logger.LogError(exception, "POS terminal enrollment dependency {Dependency} is unavailable.", exception.Dependency);
            return Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "A required POS dependency is temporarily unavailable.");
        }
    }
}

public sealed record EnrollTerminalRequest(Guid BranchId, Guid StoreId, Guid TerminalId, string Code, string DeviceType);
