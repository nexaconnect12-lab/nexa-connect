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
[Route("bff/customer/day-close-late-work")]
public sealed class DayLateWorkController(TenantSelectionCookie cookie,BffAccessTokenService tokens,IConfiguration configuration,
    CustomerLateWork application,ILogger<DayLateWorkController> logger):ControllerBase
{
    [HttpGet("csrf")]
    public IActionResult Csrf([FromServices] IAntiforgery antiforgery)=>Ok(new{requestToken=antiforgery.GetAndStoreTokens(HttpContext).RequestToken});
    [HttpGet]
    public Task<IActionResult> List([FromQuery] Guid branchId,[FromQuery] DateOnly businessDate,[FromQuery]string source,[FromQuery]string? cursor=null,[FromQuery]int limit=25,CancellationToken ct=default)=>Forward(new(branchId,businessDate,source,Cursor:cursor,Limit:limit),"list",ct);
    [HttpGet("{id:guid}")]
    public Task<IActionResult> Detail(Guid id,[FromQuery]Guid branchId,[FromQuery]DateOnly businessDate,[FromQuery]string source,CancellationToken ct)=>Forward(new(branchId,businessDate,source,id),"detail",ct);
    [HttpPost,ValidateAntiForgeryToken,RequestSizeLimit(4096)]
    public Task<IActionResult> Review(LateWorkRequest request,CancellationToken ct)=>Forward(request,"review",ct);
    private async Task<IActionResult> Forward(LateWorkRequest request,string action,CancellationToken ct)
    {
        var tenant=cookie.Unprotect(Request.Cookies["__Host-nexa-customer-tenant"]);
        if(tenant is null || tenant.SubjectId!=User.FindFirstValue("sub")){logger.LogWarning("Late-work BFF tenant rejected");return Unauthorized();}
        try
        {
            var settings=configuration.GetRequiredSection("Bff");
            var token=await tokens.GetValidAccessTokenAsync(HttpContext,"CustomerCookie",settings["Authority"]!,settings["ClientId"]!,settings["ClientSecret"]!,ct);
            if(string.IsNullOrWhiteSpace(token))return Unauthorized();
            using var response=await application.ExecuteAsync(tenant,token,request,action,ct);
            if(!response.IsSuccessStatusCode){logger.LogWarning("Late-work BFF rejected with status {StatusCode}",(int)response.StatusCode);return StatusCode((int)response.StatusCode,new{title="Late-work unavailable. Load the saved decision before retrying."});}
            var json=await BoundedJson.ReadAsync<JsonElement>(response,128*1024,ct);return Content(json.GetRawText(),"application/json",System.Text.Encoding.UTF8);
        }
        catch(Exception e)when(e is HttpRequestException or JsonException or InvalidOperationException || e is OperationCanceledException && !ct.IsCancellationRequested)
        {logger.LogWarning("Late-work BFF unavailable; category {Category}",e.GetType().Name);return StatusCode(503);}
    }
}
