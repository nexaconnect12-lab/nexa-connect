using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexaConnect.Contracts.Catalog;
using NexaConnect.Infrastructure.Authentication;

namespace NexaConnect.CustomerBff.Controllers;

[ApiController,Authorize(Policy="CustomerSession"),ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
[Route("bff/customer/barcodes")]
public sealed class CustomerBarcodesController(TenantSelectionCookie cookie,BffAccessTokenService tokens,IConfiguration configuration,
    Application.Catalog.CustomerBarcodeService service,ILogger<CustomerBarcodesController> logger):ControllerBase
{
    [HttpGet("csrf")]public IActionResult Csrf([FromServices]IAntiforgery antiforgery)=>Ok(new{requestToken=antiforgery.GetAndStoreTokens(HttpContext).RequestToken});
    [HttpGet("branches/{branch:guid}/products/{product:guid}")]
    public Task<IActionResult> Get(Guid branch,Guid product,CancellationToken ct)=>Forward(branch,product,null,ct);
    [HttpPut("branches/{branch:guid}/products/{product:guid}"),ValidateAntiForgeryToken,RequestSizeLimit(4096)]
    public Task<IActionResult> Save(Guid branch,Guid product,SetProductBarcode update,CancellationToken ct)=>Forward(branch,product,update,ct);
    private async Task<IActionResult> Forward(Guid branch,Guid product,SetProductBarcode? update,CancellationToken ct)
    {
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromSeconds(20));ct=deadline.Token;
        var tenant=cookie.Unprotect(Request.Cookies["__Host-nexa-customer-tenant"]);
        if(tenant is null||tenant.SubjectId!=User.FindFirst("sub")?.Value){logger.LogWarning("Barcode BFF tenant denied");return Unauthorized();}
        try{
            var settings=configuration.GetRequiredSection("Bff");
            var token=await tokens.GetValidAccessTokenAsync(HttpContext,"CustomerCookie",settings["Authority"]!,settings["ClientId"]!,settings["ClientSecret"]!,ct);
            if(string.IsNullOrWhiteSpace(token))return Unauthorized();
            using var response=await service.ExecuteAsync(tenant,branch,product,update,token,ct);
            if(!response.IsSuccessStatusCode){logger.LogWarning("Barcode BFF boundary rejected {StatusCode}",(int)response.StatusCode);return StatusCode((int)response.StatusCode,new{title="Barcode request rejected. Check access or duplicates, then reload before saving."});}
            await response.Content.LoadIntoBufferAsync(4096,ct);return Content(await response.Content.ReadAsStringAsync(ct),"application/json");
        }catch(Exception e)when(e is HttpRequestException or System.Text.Json.JsonException||e is OperationCanceledException&&!HttpContext.RequestAborted.IsCancellationRequested)
        {logger.LogWarning("Barcode BFF dependency unavailable");return StatusCode(503);}
    }
}
