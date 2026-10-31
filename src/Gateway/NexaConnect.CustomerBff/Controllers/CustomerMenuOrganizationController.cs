using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexaConnect.Contracts.Catalog;
using NexaConnect.CustomerBff.Application.Catalog;
using NexaConnect.Infrastructure.Authentication;
namespace NexaConnect.CustomerBff.Controllers;
[ApiController,Authorize(Policy="CustomerSession"),ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
[Route("bff/customer/menu-organization")]
public sealed class CustomerMenuOrganizationController(TenantSelectionCookie cookie,BffAccessTokenService tokens,IConfiguration configuration,CustomerMenuOrganization service,ILogger<CustomerMenuOrganizationController> logger):ControllerBase
{
    [HttpGet("csrf")]public IActionResult Csrf([FromServices]IAntiforgery antiforgery)=>Ok(new{requestToken=antiforgery.GetAndStoreTokens(HttpContext).RequestToken});
    [HttpGet("branches/{branch:guid}")]public Task<IActionResult> Read(Guid branch,CancellationToken ct)=>Forward(branch,null,ct);
    [HttpPut("branches/{branch:guid}"),ValidateAntiForgeryToken,RequestSizeLimit(65536)]public Task<IActionResult> Save(Guid branch,UpdateMenuOrganization input,CancellationToken ct)=>Forward(branch,input,ct);
    private async Task<IActionResult> Forward(Guid branch,UpdateMenuOrganization? input,CancellationToken ct)
    {
        var tenant=cookie.Unprotect(Request.Cookies["__Host-nexa-customer-tenant"]);
        if(tenant is null||tenant.SubjectId!=User.FindFirst("sub")?.Value){logger.LogWarning("Menu organization BFF tenant denied");return Unauthorized();}
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromSeconds(20));
        try{
            var settings=configuration.GetRequiredSection("Bff");
            var token=await tokens.GetValidAccessTokenAsync(HttpContext,"CustomerCookie",settings["Authority"]!,settings["ClientId"]!,settings["ClientSecret"]!,deadline.Token);
            if(string.IsNullOrWhiteSpace(token))return Unauthorized();
            using var response=await service.ExecuteAsync(tenant,branch,input,token,deadline.Token);
            if(!response.IsSuccessStatusCode){logger.LogWarning("Menu organization BFF rejected {StatusCode}",(int)response.StatusCode);return StatusCode((int)response.StatusCode is 400 or 401 or 403 or 404 or 409?(int)response.StatusCode:503,new{title="Menu categories unavailable or changed. Reload before saving."});}
            if(response.Content.Headers.ContentLength>65536)throw new InvalidDataException();await response.Content.LoadIntoBufferAsync(65536,deadline.Token);
            return Content(await response.Content.ReadAsStringAsync(deadline.Token),"application/json");
        }catch(Exception e)when(e is HttpRequestException or InvalidDataException or System.Text.Json.JsonException||e is OperationCanceledException&&!ct.IsCancellationRequested){logger.LogWarning("Menu organization BFF unavailable {Category}",e.GetType().Name);return StatusCode(503);}
    }
}
