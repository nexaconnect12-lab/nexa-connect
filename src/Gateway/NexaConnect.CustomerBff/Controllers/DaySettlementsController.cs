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
[Route("bff/customer/day-close-settlements")]
public sealed class DaySettlementsController(TenantSelectionCookie cookie,BffAccessTokenService tokens,IConfiguration configuration,
    CustomerSettlement application,ILogger<DaySettlementsController> logger):ControllerBase
{
    [HttpGet("csrf")]
    public IActionResult Csrf([FromServices] IAntiforgery antiforgery)=>Ok(new{requestToken=antiforgery.GetAndStoreTokens(HttpContext).RequestToken});
    [HttpGet]
    public Task<IActionResult> Read([FromQuery] Guid branchId,[FromQuery] DateOnly businessDate,CancellationToken ct)=>Forward(new(branchId,businessDate),"read",ct);
    [HttpPost,ValidateAntiForgeryToken,RequestSizeLimit(4096)]
    public Task<IActionResult> Finalize(SettlementRequest request,CancellationToken ct)=>Forward(request,"finalize",ct);
    private async Task<IActionResult> Forward(SettlementRequest request,string action,CancellationToken ct)
    {
        var tenant=cookie.Unprotect(Request.Cookies["__Host-nexa-customer-tenant"]);
        if(tenant is null || tenant.SubjectId!=User.FindFirstValue("sub")){logger.LogWarning("Settlement BFF tenant rejected");return Unauthorized();}
        try
        {
            var settings=configuration.GetRequiredSection("Bff");
            var token=await tokens.GetValidAccessTokenAsync(HttpContext,"CustomerCookie",settings["Authority"]!,settings["ClientId"]!,settings["ClientSecret"]!,ct);
            if(string.IsNullOrWhiteSpace(token))return Unauthorized();
            using var response=await application.ExecuteAsync(tenant,token,request,action,ct);
            if(!response.IsSuccessStatusCode){logger.LogWarning("Settlement BFF rejected with status {StatusCode}",(int)response.StatusCode);return StatusCode((int)response.StatusCode,new{title="Settlement unavailable. Load the saved decision before retrying."});}
            var json=await BoundedJson.ReadAsync<JsonElement>(response,16*1024*1024,ct);return Content(json.GetRawText(),"application/json",System.Text.Encoding.UTF8);
        }
        catch(Exception e)when(e is HttpRequestException or JsonException or InvalidOperationException || e is OperationCanceledException && !ct.IsCancellationRequested)
        {logger.LogWarning("Settlement BFF unavailable; category {Category}",e.GetType().Name);return StatusCode(503);}
    }
}
