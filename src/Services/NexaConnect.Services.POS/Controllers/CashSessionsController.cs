using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using NexaConnect.Infrastructure.Authentication;
using NexaConnect.Services.POS.Application.CashSessions;

namespace NexaConnect.Services.POS.Controllers;

[ApiController]
[Route("api/pos/v1/cash-sessions")]
public sealed class CashSessionsController(
    CashSessionApplicationService cashSessions,
    ILogger<CashSessionsController> logger) : ControllerBase
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
        if (!TryGetReplayIdentifiers(out Guid? operationId, out Guid? terminalId))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "X-Client-Operation-Id and X-Nexa-Terminal-Id are required and must both be valid non-empty UUIDs.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        try
        {
            bool created = await cashSessions.RecordMovementAsync(
                new RecordCashMovementCommand(
                    cashSessionId,
                    request.MovementType,
                    request.Amount,
                    request.ReasonCode,
                    operationId,
                    terminalId),
                subject,
                cancellationToken);
            if (operationId is not null)
            {
                logger.LogInformation(
                    created
                        ? "POS offline cash movement accepted for cash session {CashSessionId}, terminal {TerminalId}, operation {ClientOperationId}."
                        : "POS offline cash movement replay accepted for cash session {CashSessionId}, terminal {TerminalId}, operation {ClientOperationId}.",
                    cashSessionId,
                    terminalId,
                    operationId);
            }
            return Accepted();
        }
        catch (CashSessionValidationException exception)
        {
            return BadRequest(new ProblemDetails { Title = exception.Message, Status = StatusCodes.Status400BadRequest });
        }
        catch (CashSessionConflictException exception)
        {
            if (operationId is not null)
            {
                logger.LogWarning(
                    "POS offline cash movement conflict for cash session {CashSessionId}, terminal {TerminalId}, operation {ClientOperationId}.",
                    cashSessionId,
                    terminalId,
                    operationId);
            }
            return Conflict(new ProblemDetails { Title = exception.Message, Status = StatusCodes.Status409Conflict });
        }
        catch (CashSessionReplayAuthorizationException)
        {
            logger.LogWarning(
                "POS offline cash movement denied for cash session {CashSessionId}, terminal {TerminalId}, operation {ClientOperationId}.",
                cashSessionId,
                terminalId,
                operationId);
            return Forbid();
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

    private bool TryGetReplayIdentifiers(out Guid? operationId, out Guid? terminalId)
    {
        operationId = null;
        terminalId = null;
        bool hasOperation = Request.Headers.TryGetValue("X-Client-Operation-Id", out var operationValues);
        bool hasTerminal = Request.Headers.TryGetValue("X-Nexa-Terminal-Id", out var terminalValues);
        if (!hasOperation || !hasTerminal
            || !Guid.TryParse(operationValues.ToString(), out Guid parsedOperation)
            || parsedOperation == Guid.Empty
            || !Guid.TryParse(terminalValues.ToString(), out Guid parsedTerminal)
            || parsedTerminal == Guid.Empty)
        {
            return false;
        }

        operationId = parsedOperation;
        terminalId = parsedTerminal;
        return true;
    }
}

public sealed record OpenCashSessionRequest(Guid ShiftId, Guid StoreId, string? Currency, decimal OpeningAmount);
public sealed record CashMovementRequest(string MovementType, decimal Amount, string? ReasonCode);
public sealed record CloseCashSessionRequest(decimal ActualClosingAmount);
