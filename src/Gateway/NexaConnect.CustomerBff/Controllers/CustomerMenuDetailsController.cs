using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexaConnect.Contracts.Catalog;
using NexaConnect.CustomerBff.Application.Catalog;
using NexaConnect.Infrastructure.Authentication;
namespace NexaConnect.CustomerBff.Controllers;
[ApiController,Authorize(Policy="CustomerSession"),ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
[Route("bff/customer/menu-details")]
public sealed class CustomerMenuDetailsController(TenantSelectionCookie cookie,BffAccessTokenService tokens,IConfiguration configuration,CustomerMenuDetails service,ILogger<CustomerMenuDetailsController> logger):ControllerBase
{
    [HttpGet("csrf")]public IActionResult Csrf([FromServices]IAntiforgery antiforgery)=>Ok(new{requestToken=antiforgery.GetAndStoreTokens(HttpContext).RequestToken});
    [HttpGet("branches/{branch:guid}")]public Task<IActionResult> Read(Guid branch,CancellationToken ct)=>Forward(branch,null,null,ct);
    [HttpPut("branches/{branch:guid}/products/{product:guid}"),ValidateAntiForgeryToken,RequestSizeLimit(4096)]public Task<IActionResult> Save(Guid branch,Guid product,UpdateMenuDetails input,CancellationToken ct)=>Forward(branch,product,input,ct);
    private async Task<IActionResult> Forward(Guid branch,Guid? product,UpdateMenuDetails? input,CancellationToken ct)
    {
        var tenant=cookie.Unprotect(Request.Cookies["__Host-nexa-customer-tenant"]);
        if(tenant is null||tenant.SubjectId!=User.FindFirst("sub")?.Value){logger.LogWarning("Menu details BFF tenant denied");return Unauthorized();}
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromSeconds(20));
        try{
            var settings=configuration.GetRequiredSection("Bff");
            var token=await tokens.GetValidAccessTokenAsync(HttpContext,"CustomerCookie",settings["Authority"]!,settings["ClientId"]!,settings["ClientSecret"]!,deadline.Token);
            if(string.IsNullOrWhiteSpace(token))return Unauthorized();
            using var response=await service.ExecuteAsync(tenant,branch,product,input,token,deadline.Token);
            if(!response.IsSuccessStatusCode){logger.LogWarning("Menu details BFF rejected {StatusCode}",(int)response.StatusCode);return StatusCode((int)response.StatusCode is 400 or 401 or 403 or 404 or 409?(int)response.StatusCode:503,new{title="Menu details unavailable or changed. Reload before saving."});}
            if(response.Content.Headers.ContentLength>524288)throw new InvalidDataException();await response.Content.LoadIntoBufferAsync(524288,deadline.Token);
            return Content(await response.Content.ReadAsStringAsync(deadline.Token),"application/json");
        }catch(Exception e)when(e is HttpRequestException or InvalidDataException or System.Text.Json.JsonException||e is OperationCanceledException&&!ct.IsCancellationRequested){logger.LogWarning("Menu details BFF unavailable {Category}",e.GetType().Name);return StatusCode(503);}
    }
}
