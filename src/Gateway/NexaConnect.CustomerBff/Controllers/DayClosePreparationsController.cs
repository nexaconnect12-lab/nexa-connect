using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexaConnect.CustomerBff.Application.DayClose;
using NexaConnect.Infrastructure.Authentication;
namespace NexaConnect.CustomerBff.Controllers;
[ApiController,Authorize(Policy="CustomerSession"),ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
[Route("bff/customer/day-close-preparations")]
public sealed class DayClosePreparationsController(TenantSelectionCookie cookie,BffAccessTokenService tokens,
    IConfiguration configuration,CustomerDayClose application,ILogger<DayClosePreparationsController> logger):ControllerBase
{
    [HttpGet("csrf")]
    public IActionResult Csrf([FromServices] IAntiforgery antiforgery)=>Ok(new{requestToken=antiforgery.GetAndStoreTokens(HttpContext).RequestToken});
    [HttpGet]
    public Task<IActionResult> Read([FromQuery] Guid branchId,[FromQuery] DateOnly businessDate,CancellationToken ct)=>Forward(new(branchId,businessDate),false,ct);
    [HttpPost,ValidateAntiForgeryToken,RequestSizeLimit(4096)]
    public Task<IActionResult> Prepare(DayCloseRequest request,CancellationToken ct)=>Forward(request,true,ct);
    private async Task<IActionResult> Forward(DayCloseRequest request,bool prepare,CancellationToken ct)
    {
        var tenant=cookie.Unprotect(Request.Cookies["__Host-nexa-customer-tenant"]);
        if(tenant is null || tenant.SubjectId!=User.FindFirstValue("sub")){logger.LogWarning("Day-close BFF tenant session rejected");return Unauthorized();}
        try
        {
            var settings=configuration.GetRequiredSection("Bff");
            var token=await tokens.GetValidAccessTokenAsync(HttpContext,"CustomerCookie",settings["Authority"]!,settings["ClientId"]!,settings["ClientSecret"]!,ct);
            if(string.IsNullOrWhiteSpace(token))return Unauthorized();
            using var response=await application.ExecuteAsync(tenant,token,request,prepare,ct);
            if(!response.IsSuccessStatusCode){logger.LogWarning("Day-close BFF boundary rejected with status {StatusCode}",(int)response.StatusCode);return StatusCode((int)response.StatusCode,new{title="Day-close preparation unavailable. Reload before retrying."});}
            return Content(await response.Content.ReadAsStringAsync(ct),"application/json",System.Text.Encoding.UTF8);
        }
        catch(Exception e)when(e is HttpRequestException or JsonException || e is OperationCanceledException && !ct.IsCancellationRequested)
        {logger.LogWarning("Day-close BFF dependency unavailable; category {Category}",e.GetType().Name);return StatusCode(503);}
    }
}
