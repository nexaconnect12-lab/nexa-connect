using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexaConnect.CustomerBff.Application.Media;
using NexaConnect.Infrastructure.Authentication;

namespace NexaConnect.CustomerBff.Controllers;

[ApiController,Authorize(Policy="CustomerSession"),ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
[Route("bff/customer/media-library")]
public sealed class CustomerMediaLibraryController(TenantSelectionCookie cookie,BffAccessTokenService tokens,IConfiguration configuration,CustomerMediaLibrary library,ILogger<CustomerMediaLibraryController> logger):ControllerBase
{
    [HttpGet]public Task<IActionResult> Browse([FromQuery]string search="",[FromQuery]int page=1,[FromQuery]bool readyOnly=false,CancellationToken ct=default)=>Forward(null,search,page,readyOnly,ct);
    [HttpGet("assets/{asset:guid}/thumbnail")]public Task<IActionResult> Thumbnail(Guid asset,CancellationToken ct)=>Forward(asset,"",1,true,ct);
    private async Task<IActionResult> Forward(Guid? asset,string search,int page,bool readyOnly,CancellationToken ct)
    {
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromSeconds(20));ct=deadline.Token;
        var tenant=cookie.Unprotect(Request.Cookies["__Host-nexa-customer-tenant"]);
        if(tenant is null||tenant.SubjectId!=User.FindFirst("sub")?.Value){logger.LogWarning("Media library BFF tenant denied");return Unauthorized();}
        try
        {
            var settings=configuration.GetRequiredSection("Bff");
            string? token=await tokens.GetValidAccessTokenAsync(HttpContext,"CustomerCookie",settings["Authority"]!,settings["ClientId"]!,settings["ClientSecret"]!,ct);
            if(string.IsNullOrWhiteSpace(token))return Unauthorized();
            using var response=await library.ExecuteAsync(tenant,asset,search,page,readyOnly,token,ct);
            if(!response.IsSuccessStatusCode){logger.LogWarning("Media library BFF boundary rejected {StatusCode}",(int)response.StatusCode);return StatusCode((int)response.StatusCode);}
            string mime=asset.HasValue?"image/png":"application/json";long limit=asset.HasValue?524288:98304;
            if(response.Content.Headers.ContentType?.MediaType!=mime||response.Content.Headers.ContentLength>limit)throw new InvalidDataException();
            await response.Content.LoadIntoBufferAsync(limit,ct);
            Response.Headers.XContentTypeOptions="nosniff";
            return asset.HasValue?File(await response.Content.ReadAsByteArrayAsync(ct),mime):Content(await response.Content.ReadAsStringAsync(ct),mime);
        }
        catch(ArgumentException){return BadRequest();}
        catch(Exception e)when(e is HttpRequestException or IOException or InvalidDataException or System.Text.Json.JsonException||e is OperationCanceledException&&!HttpContext.RequestAborted.IsCancellationRequested)
        {logger.LogWarning("Media library BFF unavailable {Category}",e.GetType().Name);return StatusCode(503);}
    }
}
