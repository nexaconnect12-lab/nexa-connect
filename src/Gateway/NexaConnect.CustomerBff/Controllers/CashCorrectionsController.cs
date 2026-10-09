using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexaConnect.CustomerBff.Application.DayClose;
using NexaConnect.Infrastructure.Authentication;
using NexaConnect.Infrastructure.Http;
namespace NexaConnect.CustomerBff.Controllers;

[ApiController,Authorize(Policy="CustomerSession"),ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
[Route("bff/customer/late-cash-corrections")]
public sealed class CashCorrectionsController(TenantSelectionCookie cookie,BffAccessTokenService tokens,IConfiguration configuration,
    CustomerCashCorrection application,ILogger<CashCorrectionsController> logger):ControllerBase
{
    [HttpGet("csrf")]
    public IActionResult Csrf([FromServices] IAntiforgery antiforgery)=>Ok(new{requestToken=antiforgery.GetAndStoreTokens(HttpContext).RequestToken});
    [HttpGet]
    public Task<IActionResult> Read([FromQuery] Guid branchId,[FromQuery] DateOnly businessDate,[FromQuery]Guid workId,CancellationToken ct)=>Forward(new(branchId,businessDate,workId),"preview",ct);
    [HttpPost,ValidateAntiForgeryToken,RequestSizeLimit(4096)]
    public Task<IActionResult> Post(CashCorrectionRequest request,CancellationToken ct)=>Forward(request,"post",ct);
    private async Task<IActionResult> Forward(CashCorrectionRequest request,string action,CancellationToken ct)
    {
        var tenant=cookie.Unprotect(Request.Cookies["__Host-nexa-customer-tenant"]);
        if(tenant is null || tenant.SubjectId!=User.FindFirstValue("sub")){logger.LogWarning("Cash correction BFF tenant rejected");return Unauthorized();}
        try
        {
            var settings=configuration.GetRequiredSection("Bff");
            var token=await tokens.GetValidAccessTokenAsync(HttpContext,"CustomerCookie",settings["Authority"]!,settings["ClientId"]!,settings["ClientSecret"]!,ct);
            if(string.IsNullOrWhiteSpace(token))return Unauthorized();
            using var response=await application.ExecuteAsync(tenant,token,request,action,ct);
            if(!response.IsSuccessStatusCode){logger.LogWarning("Cash correction BFF rejected with status {StatusCode}",(int)response.StatusCode);return StatusCode((int)response.StatusCode,new{title="Cash correction unavailable. Load the saved decision before retrying."});}
            var json=await BoundedJson.ReadAsync<JsonElement>(response,128*1024,ct);return Content(json.GetRawText(),"application/json",System.Text.Encoding.UTF8);
        }
        catch(Exception e)when(e is HttpRequestException or JsonException or InvalidOperationException || e is OperationCanceledException && !ct.IsCancellationRequested)
        {logger.LogWarning("Cash correction BFF unavailable; category {Category}",e.GetType().Name);return StatusCode(503);}
    }
}
