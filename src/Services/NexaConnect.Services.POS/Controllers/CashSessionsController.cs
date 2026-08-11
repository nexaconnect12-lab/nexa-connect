using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using NexaConnect.Infrastructure.Authentication;
using NexaConnect.Services.POS.Application.CashSessions;

namespace NexaConnect.Services.POS.Controllers;

[ApiController]
[Route("api/pos/v1/cash-sessions")]
public sealed class CashSessionsController(CashSessionApplicationService cashSessions) : ControllerBase
{
    [HttpPost("open")]
    public async Task<IActionResult> OpenAsync(OpenCashSessionRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetSubject(out string subject)) return Unauthorized();

        try
        {
            Guid id = await cashSessions.OpenAsync(
                new OpenCashSessionCommand(request.ShiftId, request.StoreId, request.Currency, request.OpeningAmount),
                subject,
                cancellationToken);
            return Ok(new { cashSessionId = id, openedBy = subject });
        }
        catch (CashSessionValidationException exception)
        {
            return BadRequest(new ProblemDetails { Title = exception.Message, Status = StatusCodes.Status400BadRequest });
        }
        catch (CashSessionConflictException exception)
        {
            return Conflict(new ProblemDetails { Title = exception.Message, Status = StatusCodes.Status409Conflict });
        }
    }

    [HttpPost("{cashSessionId:guid}/movements")]
    public async Task<IActionResult> MovementAsync(
        Guid cashSessionId,
        CashMovementRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetSubject(out string subject)) return Unauthorized();

        try
        {
            await cashSessions.RecordMovementAsync(
                new RecordCashMovementCommand(cashSessionId, request.MovementType, request.Amount, request.ReasonCode),
                subject,
                cancellationToken);
            return Accepted();
        }
        catch (CashSessionValidationException exception)
        {
            return BadRequest(new ProblemDetails { Title = exception.Message, Status = StatusCodes.Status400BadRequest });
        }
        catch (CashSessionConflictException exception)
        {
            return Conflict(new ProblemDetails { Title = exception.Message, Status = StatusCodes.Status409Conflict });
        }
    }

    [HttpPost("{cashSessionId:guid}/close")]
    public async Task<IActionResult> CloseAsync(
        Guid cashSessionId,
        CloseCashSessionRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetSubject(out string subject)) return Unauthorized();

        try
        {
            await cashSessions.CloseAsync(cashSessionId, request.ActualClosingAmount, subject, cancellationToken);
            return NoContent();
        }
        catch (CashSessionValidationException exception)
        {
            return BadRequest(new ProblemDetails { Title = exception.Message, Status = StatusCodes.Status400BadRequest });
        }
        catch (CashSessionConflictException exception)
        {
            return Conflict(new ProblemDetails { Title = exception.Message, Status = StatusCodes.Status409Conflict });
        }
    }

    private bool TryGetSubject(out string subject)
    {
        subject = User.FindFirst(NexaAuthenticationDefaults.SubjectClaim)?.Value
            ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? "";
        return User.Identity?.IsAuthenticated == true && !string.IsNullOrWhiteSpace(subject);
    }
}

public sealed record OpenCashSessionRequest(Guid ShiftId, Guid StoreId, string? Currency, decimal OpeningAmount);
public sealed record CashMovementRequest(string MovementType, decimal Amount, string? ReasonCode);
public sealed record CloseCashSessionRequest(decimal ActualClosingAmount);
