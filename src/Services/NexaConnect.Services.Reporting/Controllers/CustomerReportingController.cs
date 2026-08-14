using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexaConnect.Contracts.Platform;
using NexaConnect.Services.Reporting.Application;

namespace NexaConnect.Services.Reporting.Controllers;

[ApiController, Authorize(Roles = "customer-owner,customer-admin,customer-manager,customer-viewer")]
[Route("api/reporting/v1/customer/organizations/{organizationId:guid}")]
public sealed class CustomerReportingController(ReportingQueries queries,ActivityService activity, IReportingCustomerAuthorizer authorizer, ILogger<CustomerReportingController> logger) : ControllerBase
{
    [HttpGet("dashboard")]
    public Task<IActionResult> Dashboard(Guid organizationId, [FromQuery] Guid? branchId, [FromQuery] DateTimeOffset? fromUtc, [FromQuery] DateTimeOffset? toUtc, CancellationToken cancellationToken) => Execute(organizationId, branchId, ProductPermissions.ReportingDashboardRead, () => queries.DashboardAsync(organizationId, branchId, fromUtc, toUtc, cancellationToken), cancellationToken);

    [HttpGet("reports/sales")]
    public Task<IActionResult> Sales(Guid organizationId, [FromQuery] Guid? branchId, [FromQuery] DateTimeOffset? fromUtc, [FromQuery] DateTimeOffset? toUtc, CancellationToken cancellationToken) => Execute(organizationId, branchId, ProductPermissions.ReportingSalesRead, () => queries.SalesAsync(organizationId, branchId, fromUtc, toUtc, cancellationToken), cancellationToken);

    [HttpGet("activity")]
    public Task<IActionResult> Activity(Guid organizationId,[FromQuery]string? actorSubjectId,[FromQuery]string? action,[FromQuery]string? cursor,[FromQuery]int limit=50,CancellationToken cancellationToken=default)=>Execute(organizationId,null,ProductPermissions.ReportingActivityRead,()=>activity.QueryAsync(organizationId,"nexa_connect",actorSubjectId,action,cursor,limit,cancellationToken),cancellationToken);

    private async Task<IActionResult> Execute<T>(Guid organizationId, Guid? branchId, string permission, Func<Task<T>> query, CancellationToken cancellationToken)
    {
        string? actor = User.FindFirstValue("sub") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        bool granted = actor is not null && await authorizer.IsGrantedAsync(organizationId, branchId, permission, Request.Headers.Authorization.ToString(), cancellationToken);
        if (!granted) { logger.LogWarning("Customer reporting authorization denied for organization {OrganizationId}, branch {BranchId}, permission {Permission}, actor {ActorSubjectId}", organizationId, branchId, permission, actor); return Forbid(); }
        try { return Ok(await query()); }
        catch (ArgumentException exception) { return BadRequest(new ProblemDetails { Title = exception.Message, Status = 400 }); }
        catch (MixedReportingCurrencyException exception) { return Conflict(new ProblemDetails { Title = exception.Message, Status = 409 }); }
        catch (Exception exception) { logger.LogError(exception, "Customer reporting query failed for organization {OrganizationId}, branch {BranchId}, permission {Permission}", organizationId, branchId, permission); throw; }
    }
}
