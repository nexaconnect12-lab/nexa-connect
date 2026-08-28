using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using NexaConnect.Contracts.Platform;
using NexaConnect.Infrastructure.Authorization;
using NexaConnect.Services.Order.Application.PaymentReviews;
using NexaConnect.Services.Order.Application.Tenant;

namespace NexaConnect.Services.Order.Controllers;

[ApiController]
[Route("api/order/v1/payment-reviews")]
public sealed class PaymentReviewsController(PaymentReviewApplicationService reviews,IOrderTenantAuthorizer tenantAuthorizer,
    ILogger<PaymentReviewsController> logger):ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyCollection<PaymentReviewCase>>> List([FromQuery]Guid organizationId,[FromQuery]Guid branchId,[FromQuery]int limit=100,CancellationToken cancellationToken=default)
    {
        if(!await HasAccessAsync(organizationId,branchId,ProductPermissions.OrderPaymentReviewRead,cancellationToken))return Forbid();
        try{return Ok(await reviews.ListAsync(organizationId,branchId,limit,cancellationToken));}
        catch(ArgumentException exception){return BadRequest(new{error=exception.Message});}
    }

    [HttpGet("{orderId:guid}")]
    public async Task<ActionResult<PaymentReviewCase>> Get(Guid orderId,[FromQuery]Guid organizationId,CancellationToken cancellationToken)
    {
        PaymentReviewCase? review=await reviews.GetAsync(organizationId,orderId,cancellationToken);if(review is null)return NotFound();
        return await HasAccessAsync(organizationId,review.BranchId,ProductPermissions.OrderPaymentReviewRead,cancellationToken)?Ok(review):NotFound();
    }

    [HttpPost("{orderId:guid}/resolve")]
    public async Task<ActionResult<PaymentReviewCase>> Resolve(Guid orderId,ResolvePaymentReviewRequest request,CancellationToken cancellationToken)
    {
        PaymentReviewCase? existing=await reviews.GetAsync(request.OrganizationId,orderId,cancellationToken);if(existing is null)return NotFound();
        Guid? decisionId=await GetDecisionAsync(request.OrganizationId,existing.BranchId,ProductPermissions.OrderPaymentReviewResolve,cancellationToken);
        if(decisionId is null)return Forbid();
        string actor=User.FindFirstValue("sub")??User.FindFirstValue(ClaimTypes.NameIdentifier)??User.FindFirstValue("azp")??string.Empty;
        try
        {
            PaymentReviewCase? result=await reviews.ResolveAsync(new(request.OrganizationId,orderId,request.Resolution,request.Reason,
                request.ExpectedConcurrencyVersion,actor,request.CorrelationId??Guid.NewGuid(),decisionId.Value),cancellationToken);
            logger.LogInformation("Payment review {OrderId} resolved as {Resolution} for organization {OrganizationId}",orderId,request.Resolution,request.OrganizationId);
            return result is null?NotFound():Ok(result);
        }
        catch(ArgumentException exception){return BadRequest(new{error=exception.Message});}
        catch(InvalidOperationException exception)when(exception.Message.Contains("concurrency",StringComparison.OrdinalIgnoreCase)){return Conflict(new{error=exception.Message});}
    }

    private async Task<bool> HasAccessAsync(Guid organizationId,Guid branchId,string permission,CancellationToken cancellationToken)
        =>await GetDecisionAsync(organizationId,branchId,permission,cancellationToken) is not null;

    private async Task<Guid?> GetDecisionAsync(Guid organizationId,Guid branchId,string permission,CancellationToken cancellationToken)
    {
        return Guid.TryParse(Request.Headers[TenantContextHeaders.OrganizationId],out Guid contextOrganization)&&contextOrganization==organizationId
            &&string.Equals(Request.Headers[TenantContextHeaders.ApplicationCode],"nexa_connect",StringComparison.Ordinal)
            &&Request.Headers.TryGetValue("Authorization",out var authorization)
            ?await tenantAuthorizer.GetBranchDecisionAsync(organizationId,branchId,permission,authorization.ToString(),cancellationToken):null;
    }
}

public sealed record ResolvePaymentReviewRequest(Guid OrganizationId,string Resolution,string Reason,long ExpectedConcurrencyVersion,Guid? CorrelationId=null);
