using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using NexaConnect.Contracts.RestaurantQr;
using NexaConnect.CustomerBff.Application.Qr;
using NexaConnect.Infrastructure.Authentication;
namespace NexaConnect.CustomerBff.Controllers;

[ApiController,Authorize(Policy="CustomerSession"),Route("bff/customer/qr-requests")]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class QrReviewController(TenantSelectionCookie cookie,BffAccessTokenService tokens,IConfiguration configuration,
    IOptions<QrGuestOptions> settings,QrGatewayService service,ILogger<QrReviewController> logger):ControllerBase
{
    private bool Enabled=>settings.Value.Enabled&&settings.Value.MenuEnabled&&settings.Value.RequestsEnabled;
    [HttpGet("csrf")]public IActionResult Csrf([FromServices]IAntiforgery csrf)=>Enabled?Ok(new{requestToken=csrf.GetAndStoreTokens(HttpContext).RequestToken,acceptanceAvailable=settings.Value.AcceptanceEnabled}):NotFound();
    [HttpGet("branches/{branch:guid}")]public Task<IActionResult> List(Guid branch,[FromQuery]string state="pending",CancellationToken ct=default)=>Send(branch,state,null,null,ct);
    [HttpPost("branches/{branch:guid}/{id:guid}/reject"),ValidateAntiForgeryToken,RequestSizeLimit(1024)]public Task<IActionResult> Reject(Guid branch,Guid id,QrRejection input,CancellationToken ct)=>Send(branch,"pending",id,input,ct);
    [HttpPost("branches/{branch:guid}/{id:guid}/accept"),ValidateAntiForgeryToken,RequestSizeLimit(1024)]public Task<IActionResult> Accept(Guid branch,Guid id,QrAcceptance input,CancellationToken ct)=>!settings.Value.AcceptanceEnabled?Task.FromResult<IActionResult>(NotFound()):Send(branch,"pending",id,input,ct);
    private async Task<IActionResult> Send(Guid branch,string state,Guid? id,object? input,CancellationToken ct)
    {
        if(!Enabled)return NotFound();
        if(state is not ("pending" or "price_review" or "accepted" or "rejected" or "expired"))return BadRequest();
        var tenant=cookie.Unprotect(Request.Cookies["__Host-nexa-customer-tenant"]);
        if(tenant is null||tenant.SubjectId!=User.FindFirstValue("sub"))return Unauthorized();
        var bff=configuration.GetRequiredSection("Bff");
        string? bearer=await tokens.GetValidAccessTokenAsync(HttpContext,"CustomerCookie",bff["Authority"]!,bff["ClientId"]!,bff["ClientSecret"]!,ct);
        if(string.IsNullOrWhiteSpace(bearer))return Unauthorized();
        try
        {
            var reply=input is QrAcceptance a?await service.AcceptAsync(tenant,bearer,branch,id!.Value,a,ct):await service.ReviewAsync(tenant,bearer,branch,state,id,input as QrRejection,ct);
            if(reply.Status is <200 or >=300){logger.LogWarning("QR review downstream rejected; status {Status}",reply.Status);return StatusCode(QrGuestEntryController.SafeStatus(reply.Status));}
            return input is null?Ok(reply.Body?.Deserialize<QrRequestList>(JsonSerializerOptions.Web)??throw new JsonException()):Ok(reply.Body?.Deserialize<QrRequestView>(JsonSerializerOptions.Web)??throw new JsonException());
        }
        catch(Exception e)when(e is HttpRequestException or JsonException||e is OperationCanceledException&&!ct.IsCancellationRequested)
        {logger.LogWarning("QR review dependency unavailable");return StatusCode(503);}
    }
}
