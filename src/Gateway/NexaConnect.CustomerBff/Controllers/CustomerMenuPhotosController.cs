using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexaConnect.Contracts.Catalog;
using NexaConnect.CustomerBff.Application.Catalog;
using NexaConnect.Infrastructure.Authentication;
namespace NexaConnect.CustomerBff.Controllers;
[ApiController,Authorize(Policy="CustomerSession"),ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
[Route("bff/customer/menu-photos")]
public sealed class CustomerMenuPhotosController(TenantSelectionCookie cookie,BffAccessTokenService tokens,IConfiguration configuration,CustomerMenuPhotos service,ILogger<CustomerMenuPhotosController> logger):ControllerBase
{
    [HttpGet("csrf")]public IActionResult Csrf([FromServices]IAntiforgery antiforgery)=>Ok(new{requestToken=antiforgery.GetAndStoreTokens(HttpContext).RequestToken});
    [HttpGet("branches/{branch:guid}/products/{product:guid}")]public Task<IActionResult> Get(Guid branch,Guid product,CancellationToken ct)=>Forward(branch,product,false,null,null,ct);
    [HttpPut("branches/{branch:guid}/products/{product:guid}"),ValidateAntiForgeryToken,RequestSizeLimit(4096)]public Task<IActionResult> Save(Guid branch,Guid product,SetMenuPhoto input,CancellationToken ct)=>Forward(branch,product,false,null,input,ct);
    [HttpGet("branches/{branch:guid}/products/{product:guid}/image")]public Task<IActionResult> Photo(Guid branch,Guid product,CancellationToken ct)=>Forward(branch,product,true,null,null,ct);
    [HttpGet("branches/{branch:guid}/products/{product:guid}/preview/{asset:guid}")]public Task<IActionResult> Preview(Guid branch,Guid product,Guid asset,CancellationToken ct)=>Forward(branch,product,true,asset,null,ct);
    private async Task<IActionResult> Forward(Guid branch,Guid product,bool photo,Guid? preview,SetMenuPhoto? input,CancellationToken ct)
    {
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromSeconds(20));ct=deadline.Token;
        var tenant=cookie.Unprotect(Request.Cookies["__Host-nexa-customer-tenant"]);
        if(tenant is null||tenant.SubjectId!=User.FindFirst("sub")?.Value){logger.LogWarning("Menu photo BFF tenant denied");return Unauthorized();}
        try{
            var settings=configuration.GetRequiredSection("Bff");
            var token=await tokens.GetValidAccessTokenAsync(HttpContext,"CustomerCookie",settings["Authority"]!,settings["ClientId"]!,settings["ClientSecret"]!,ct);
            if(string.IsNullOrWhiteSpace(token))return Unauthorized();
            using var response=await service.ExecuteAsync(tenant,branch,product,photo,preview,input,token,ct);
            if(!response.IsSuccessStatusCode){logger.LogWarning("Menu photo BFF boundary rejected {StatusCode}",(int)response.StatusCode);return StatusCode((int)response.StatusCode,new{title="Menu image unavailable or changed. Reload and check access before saving."});}
            await response.Content.LoadIntoBufferAsync(photo?524288:4096,ct);
            if(photo){if(response.Content.Headers.ContentType?.MediaType!="image/png")throw new InvalidDataException();Response.Headers.XContentTypeOptions="nosniff";return File(await response.Content.ReadAsByteArrayAsync(ct),"image/png");}
            return Content(await response.Content.ReadAsStringAsync(ct),"application/json");
        }catch(Exception e)when(e is HttpRequestException or InvalidDataException or System.Text.Json.JsonException||e is OperationCanceledException&&!HttpContext.RequestAborted.IsCancellationRequested){logger.LogWarning("Menu photo BFF unavailable {Category}",e.GetType().Name);return StatusCode(503);}
    }
}
