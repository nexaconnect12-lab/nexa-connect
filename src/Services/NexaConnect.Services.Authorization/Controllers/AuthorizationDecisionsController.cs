using Microsoft.AspNetCore.Mvc;
using NexaConnect.Infrastructure.Authentication;
using NexaConnect.Services.Authorization.Application.Decisions;
using ApplicationDecision = NexaConnect.Services.Authorization.Application.Decisions.AuthorizationDecision;

[ApiController]
[Route("api/authorization/v1/decisions")]
public sealed class AuthorizationDecisionsController(
    IAuthorizationDecisionService decisionService,
    ILogger<AuthorizationDecisionsController> logger) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<AuthorizationDecisionResponse>> DecideAsync(
        AuthorizationDecisionRequest request, CancellationToken cancellationToken)
    {
        string? subjectId = User.FindFirst(NexaAuthenticationDefaults.SubjectClaim)?.Value
            ?? User.FindFirst(NexaAuthenticationDefaults.UsernameClaim)?.Value;
        if (string.IsNullOrWhiteSpace(subjectId)) return Forbid();
        ApplicationDecision decision = await decisionService.DecideAsync(subjectId, request.OrganizationId, request.RestaurantId,
            request.BranchId, request.Permission, request.Amount, request.Currency, cancellationToken);
        logger.Log(decision.Granted ? LogLevel.Information : LogLevel.Warning,
            "Authorization decision {DecisionId}: granted {Granted}.", decision.Id, decision.Granted);
        return Ok(new AuthorizationDecisionResponse(decision.Id, decision.Granted, decision.EvaluatedLimit));
    }
}

public sealed record AuthorizationDecisionRequest(Guid OrganizationId, Guid? RestaurantId, Guid? BranchId,
    string Permission, decimal? Amount, string? Currency);
public sealed record AuthorizationDecisionResponse(Guid DecisionId, bool Granted, decimal? EvaluatedLimit);
