using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexaConnect.Infrastructure.Authentication;
using NexaConnect.Services.POS.Application.CashReviews;
using NexaConnect.Services.POS.Application.Shifts;

namespace NexaConnect.Services.POS.Controllers;

[ApiController]
[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("api/pos/v1/cash-reviews")]
public sealed class CashReviewsController(
    CashReviewApplicationService reviews,
    ILogger<CashReviewsController> logger) : ControllerBase
{
    [HttpGet("access")]
    public async Task<IActionResult> AccessAsync([FromQuery] Guid organizationId, [FromQuery] Guid branchId,
        [FromQuery] Guid storeId, CancellationToken cancellationToken)
    {
        PosUserContext? user = GetUserContext();
        if (user is null) return Unauthorized();
        try
        {
            return Ok(await reviews.AccessAsync(organizationId, branchId, storeId, user, cancellationToken));
        }
        catch (CashReviewValidationException exception) { return BadRequest(Problem(exception.Message, 400)); }
        catch (CashReviewAuthorizationException)
        {
            logger.LogWarning("POS cash-review access denied for branch {BranchId} and store {StoreId}.", branchId, storeId);
            return Forbid();
        }
        catch (CashReviewDependencyException exception) { return DependencyUnavailable(exception); }
    }

    [HttpGet]
    public async Task<IActionResult> ListAsync([FromQuery] Guid organizationId, [FromQuery] Guid branchId,
        [FromQuery] Guid storeId, [FromQuery] DateTimeOffset fromUtc, [FromQuery] DateTimeOffset toUtc,
        [FromQuery] string? cursor = null, [FromQuery] int limit = 50,
        CancellationToken cancellationToken = default)
    {
        PosUserContext? user = GetUserContext();
        if (user is null) return Unauthorized();
        try
        {
            return Ok(await reviews.ListAsync(organizationId, branchId, storeId, fromUtc, toUtc,
                cursor, limit, user, cancellationToken));
        }
        catch (CashReviewValidationException exception) { return BadRequest(Problem(exception.Message, 400)); }
        catch (CashReviewAuthorizationException)
        {
            logger.LogWarning("POS cash-review list denied for branch {BranchId} and store {StoreId}.", branchId, storeId);
            return Forbid();
        }
        catch (CashReviewDependencyException exception) { return DependencyUnavailable(exception); }
    }

    [HttpGet("{cashSessionId:guid}")]
    public async Task<IActionResult> GetAsync(Guid cashSessionId, [FromQuery] Guid organizationId,
        [FromQuery] Guid branchId, [FromQuery] Guid storeId, CancellationToken cancellationToken)
    {
        PosUserContext? user = GetUserContext();
        if (user is null) return Unauthorized();
        try
        {
            return Ok(await reviews.GetAsync(organizationId, branchId, storeId, cashSessionId,
                user, cancellationToken));
        }
        catch (CashReviewValidationException exception) { return BadRequest(Problem(exception.Message, 400)); }
        catch (CashReviewAuthorizationException)
        {
            logger.LogWarning("POS cash-review detail denied or absent for cash session {CashSessionId}.", cashSessionId);
            return NotFound();
        }
        catch (CashReviewNotFoundException)
        {
            logger.LogWarning("POS cash-review detail denied or absent for cash session {CashSessionId}.", cashSessionId);
            return NotFound();
        }
        catch (CashReviewDependencyException exception) { return DependencyUnavailable(exception); }
    }

    [HttpPost("{cashSessionId:guid}/decisions")]
    public async Task<IActionResult> ResolveAsync(Guid cashSessionId, ResolveCashReviewRequest request,
        CancellationToken cancellationToken)
    {
        PosUserContext? user = GetUserContext();
        if (user is null) return Unauthorized();
        try
        {
            CashReviewDetail detail = await reviews.ResolveAsync(new ResolveCashReviewCommand(
                request.OrganizationId, request.BranchId, request.StoreId, cashSessionId,
                request.Decision, request.Reason, request.ExpectedSessionVersion,
                request.ExpectedReviewVersion, request.IdempotencyKey), user, cancellationToken);
            logger.LogInformation(
                "POS cash-review decision committed for cash session {CashSessionId}, branch {BranchId}, store {StoreId} and review version {ReviewVersion}.",
                cashSessionId, request.BranchId, request.StoreId, detail.Session.ReviewVersion);
            return Ok(detail);
        }
        catch (CashReviewValidationException exception) { return BadRequest(Problem(exception.Message, 400)); }
        catch (CashReviewAuthorizationException)
        {
            logger.LogWarning("POS cash-review decision denied for cash session {CashSessionId}.", cashSessionId);
            return Forbid();
        }
        catch (CashReviewNotFoundException) { return NotFound(); }
        catch (CashReviewConflictException exception)
        {
            logger.LogWarning("POS cash-review decision conflicted for cash session {CashSessionId}.", cashSessionId);
            return Conflict(Problem(exception.Message, 409));
        }
        catch (CashReviewDuplicateOperationException exception)
        {
            logger.LogWarning("POS cash-review operation identity conflicted for cash session {CashSessionId}.", cashSessionId);
            return Conflict(Problem(exception.Message, 409));
        }
        catch (CashReviewDependencyException exception) { return DependencyUnavailable(exception); }
    }

    private PosUserContext? GetUserContext()
    {
        string? subject = User.FindFirst(NexaAuthenticationDefaults.SubjectClaim)?.Value
            ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        string authorization = Request.Headers.Authorization.ToString();
        const string prefix = "Bearer ";
        string? token = authorization.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? authorization[prefix.Length..].Trim()
            : null;
        return string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(token)
            ? null
            : new PosUserContext(subject, token);
    }

    private ObjectResult DependencyUnavailable(CashReviewDependencyException exception)
    {
        logger.LogError(exception, "POS cash-review dependency {Dependency} is unavailable.", exception.Dependency);
        return Problem(statusCode: 503, title: "A required POS dependency is temporarily unavailable.");
    }

    private static ProblemDetails Problem(string title, int status) => new() { Title = title, Status = status };
}

public sealed record ResolveCashReviewRequest(Guid OrganizationId, Guid BranchId, Guid StoreId,
    string Decision, string Reason, long ExpectedSessionVersion, long ExpectedReviewVersion,
    Guid IdempotencyKey);
