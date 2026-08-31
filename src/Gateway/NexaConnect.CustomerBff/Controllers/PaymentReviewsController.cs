using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexaConnect.Contracts.Platform;
using NexaConnect.CustomerBff.Application.PaymentReviews;
using NexaConnect.Infrastructure.Authentication;

namespace NexaConnect.CustomerBff.Controllers;

[ApiController,Authorize(Policy="CustomerSession")]
[Route("bff/customer/payment-reviews")]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class PaymentReviewsController(TenantSelectionCookie cookie,BffAccessTokenService tokens,IConfiguration configuration,CustomerPaymentReviewService reviews,ILogger<PaymentReviewsController> logger):ControllerBase
{
    [HttpGet("csrf")]
    public IActionResult Csrf([FromServices] IAntiforgery antiforgery)=>Ok(new{requestToken=antiforgery.GetAndStoreTokens(HttpContext).RequestToken});
    [HttpGet("branches/{branchId:guid}")]
    public Task<IActionResult> List(Guid branchId,CancellationToken ct)=>Forward(ReviewOperation.List,branchId,null,ct);
    [HttpGet("branches/{branchId:guid}/access")]
    public Task<IActionResult> Access(Guid branchId,CancellationToken ct)=>Forward(ReviewOperation.Access,branchId,null,ct);
    [HttpGet("{orderId:guid}")]
    public Task<IActionResult> Detail(Guid orderId,CancellationToken ct)=>Forward(ReviewOperation.Detail,orderId,null,ct);
    [HttpGet("{orderId:guid}/history")]
    public Task<IActionResult> History(Guid orderId,CancellationToken ct)=>Forward(ReviewOperation.History,orderId,null,ct);
    [HttpPost("{orderId:guid}/resolve"),ValidateAntiForgeryToken]
    [RequestSizeLimit(4096)]
    public Task<IActionResult> Resolve(Guid orderId,PaymentReviewResolutionRequest request,CancellationToken ct)=>Forward(ReviewOperation.Resolve,orderId,request,ct);

    private async Task<IActionResult> Forward(ReviewOperation operation,Guid id,PaymentReviewResolutionRequest? request,CancellationToken ct)
    {
        TenantContext? tenant=cookie.Unprotect(Request.Cookies["__Host-nexa-customer-tenant"]);
        if(tenant is null||tenant.SubjectId!=User.FindFirstValue("sub"))return Unauthorized();
        var settings=configuration.GetRequiredSection("Bff");
        string? token=await tokens.GetValidAccessTokenAsync(HttpContext,"CustomerCookie",settings["Authority"]!,settings["ClientId"]!,settings["ClientSecret"]!,ct);
        if(string.IsNullOrWhiteSpace(token))return Unauthorized();
        try
        {
            using var response=await reviews.ExecuteAsync(tenant,token,operation,id,request,ct);
            if(!response.IsSuccessStatusCode)logger.LogWarning("Payment review BFF operation {Operation} returned status {StatusCode}",operation,(int)response.StatusCode);
            // Do not forward arbitrary downstream bodies (including financial reasons) on errors.
            if(!response.IsSuccessStatusCode)return StatusCode((int)response.StatusCode,new{title="Payment Review request could not be completed."});
            return Content(await response.Content.ReadAsStringAsync(ct),"application/json",System.Text.Encoding.UTF8);
        }
        catch(HttpRequestException){logger.LogWarning("Payment review BFF dependency unavailable for operation {Operation}",operation);return StatusCode(503,new{title="Payment Review service unavailable."});}
        catch(OperationCanceledException)when(!ct.IsCancellationRequested){logger.LogWarning("Payment review BFF dependency timed out for operation {Operation}",operation);return StatusCode(503,new{title="Payment Review service timed out."});}
    }
}
