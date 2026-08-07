using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using NexaConnect.Infrastructure.Authentication;
using NexaConnect.Services.POS.Application.Shifts;
using NexaConnect.Services.POS.Infrastructure.Persistence;
using NexaConnect.Services.POS.Infrastructure.Restaurant;

namespace NexaConnect.Services.POS.Controllers;

[ApiController]
[Route("api/pos/v1/terminals")]
public sealed class TerminalsController(
    ITerminalStore terminals,
    IRestaurantScopeReader scopeReader,
    IAuthorizationDecisionClient authorization) : ControllerBase
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

        if (request.BranchId == Guid.Empty || request.StoreId == Guid.Empty || request.TerminalId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.Code) || request.DeviceType is not ("pos" or "kiosk" or "kds" or "edge"))
        {
            return BadRequest();
        }

        string authorizationHeader = Request.Headers.Authorization.ToString();
        if (!authorizationHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return Unauthorized();
        RestaurantAuthorizationScope scope = await scopeReader.GetAsync(request.BranchId, cancellationToken);
        AuthorizationDecision decision = await authorization.DecideAsync(
            new PosUserContext(subject, authorizationHeader[7..].Trim()),
            scope,
            "pos.terminal.enroll",
            cancellationToken);
        if (!decision.Granted)
        {
            return Forbid();
        }

        bool enrolled = await terminals.EnrollAsync(
            scope.OrganizationId,
            scope.RestaurantId,
            scope.BranchId,
            request.StoreId,
            request.TerminalId,
            request.Code.Trim(),
            request.DeviceType,
            cancellationToken);
        return enrolled ? Created($"api/pos/v1/terminals/{request.TerminalId:D}", new { request.TerminalId }) : NotFound();
    }
}

public sealed record EnrollTerminalRequest(Guid BranchId, Guid StoreId, Guid TerminalId, string Code, string DeviceType);
