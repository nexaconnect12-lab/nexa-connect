using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using NexaConnect.Infrastructure.Authentication;
using NexaConnect.Services.POS.Infrastructure.Persistence;

namespace NexaConnect.Services.POS.Controllers;

[ApiController]
[Route("api/pos/v1/cash-sessions")]
public sealed class CashSessionsController(ICashSessionStore store) : ControllerBase
{
    [HttpPost("open")]
    public async Task<IActionResult> OpenAsync(OpenCashSessionRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetSubject(out string subject)) return Unauthorized();
        if (request.ShiftId == Guid.Empty || request.StoreId == Guid.Empty || request.OpeningAmount < 0 ||
            !System.Text.RegularExpressions.Regex.IsMatch(request.Currency ?? "", "^[A-Za-z]{3}$"))
        {
            return BadRequest();
        }

        try
        {
            Guid id = await store.OpenAsync(
                request.ShiftId, request.StoreId, request.Currency!, request.OpeningAmount, cancellationToken);
            return Ok(new { cashSessionId = id, openedBy = subject });
        }
        catch (InvalidOperationException exception)
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
        if (cashSessionId == Guid.Empty || request.Amount <= 0 || request.MovementType is not
            ("sale" or "refund" or "pay_in" or "pay_out" or "float_adjustment"))
        {
            return BadRequest();
        }

        try
        {
            await store.RecordMovementAsync(
                cashSessionId, request.MovementType, request.Amount, subject, request.ReasonCode, cancellationToken);
            return Accepted();
        }
        catch (InvalidOperationException exception)
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
        if (!TryGetSubject(out _)) return Unauthorized();
        if (cashSessionId == Guid.Empty || request.ActualClosingAmount < 0) return BadRequest();

        try
        {
            await store.CloseAsync(cashSessionId, request.ActualClosingAmount, cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException exception)
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
