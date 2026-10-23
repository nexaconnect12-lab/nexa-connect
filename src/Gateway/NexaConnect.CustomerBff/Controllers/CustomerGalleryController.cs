using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using NexaConnect.Contracts.Media;
using NexaConnect.CustomerBff.Application.Gallery;
using NexaConnect.Infrastructure.Authentication;

namespace NexaConnect.CustomerBff.Controllers;

[ApiController,Authorize(Policy="CustomerSession"),ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
[Route("bff/customer/screensaver-gallery")]
public sealed class CustomerGalleryController(TenantSelectionCookie cookie,BffAccessTokenService tokens,IConfiguration configuration,
    CustomerGalleryService gallery,ILogger<CustomerGalleryController> logger):ControllerBase
{
    [HttpGet("csrf")] public IActionResult Csrf([FromServices]IAntiforgery antiforgery)=>Ok(new{requestToken=antiforgery.GetAndStoreTokens(HttpContext).RequestToken});
    [HttpGet("branches/{branch:guid}")] public Task<IActionResult> Get(Guid branch,CancellationToken ct)=>Forward(branch,null,ct);
    [HttpPut("branches/{branch:guid}"),ValidateAntiForgeryToken,RequestSizeLimit(16384)]
    public Task<IActionResult> Save(Guid branch,UpdateBranchGallery update,CancellationToken ct)=>Forward(branch,update,ct);
    private async Task<IActionResult> Forward(Guid branch,UpdateBranchGallery? update,CancellationToken ct)
    {
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromSeconds(20));ct=deadline.Token;
        var tenant=cookie.Unprotect(Request.Cookies["__Host-nexa-customer-tenant"]);
        if(tenant is null||tenant.SubjectId!=User.FindFirstValue("sub")){logger.LogWarning("Gallery BFF tenant denied");return Unauthorized();}
        var settings=configuration.GetRequiredSection("Bff");
        var token=await tokens.GetValidAccessTokenAsync(HttpContext,"CustomerCookie",settings["Authority"]!,settings["ClientId"]!,settings["ClientSecret"]!,ct);
        if(string.IsNullOrWhiteSpace(token))return Unauthorized();
        try{
            using var response=await gallery.ExecuteAsync(tenant,branch,update,token,ct);
            if(!response.IsSuccessStatusCode){logger.LogWarning("Gallery BFF boundary rejected {StatusCode}",(int)response.StatusCode);return StatusCode((int)response.StatusCode,new{title="Gallery request rejected. Reload before retrying."});}
            await response.Content.LoadIntoBufferAsync(16384,ct);return Content(await response.Content.ReadAsStringAsync(ct),"application/json");
        }catch(Exception e)when(e is HttpRequestException||e is OperationCanceledException&&!HttpContext.RequestAborted.IsCancellationRequested)
        {logger.LogWarning("Gallery BFF dependency unavailable");return StatusCode(503);}
    }
}
