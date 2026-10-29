using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexaConnect.Contracts.Catalog;
using NexaConnect.Contracts.Platform;
using NexaConnect.Services.Catalog.Application.Barcodes;
using NexaConnect.Services.Catalog.Application.Menu;
using NexaConnect.Services.Catalog.Domain.Barcodes;

namespace NexaConnect.Services.Catalog.Controllers;

[ApiController,Authorize(Roles="customer-owner,customer-admin,customer-user,customer-manager,customer-viewer")]
[Route("api/catalog/v1/customer/branches/{branch:guid}")]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class ProductBarcodesController(BarcodeApplication barcodes,ILogger<ProductBarcodesController> logger):ControllerBase
{
    [HttpGet("products/{product:guid}/barcode")]
    public Task<IActionResult> Read(Guid branch,Guid product,CancellationToken ct)=>Execute(org=>barcodes.ReadAsync(org,branch,product,Request.Headers.Authorization.ToString(),ct),ct);
    [HttpPut("products/{product:guid}/barcode"),RequestSizeLimit(4096)]
    public Task<IActionResult> Save(Guid branch,Guid product,SetProductBarcode update,CancellationToken ct)
    {
        string? subject=User.FindFirst("sub")?.Value;
        if(string.IsNullOrWhiteSpace(subject))return Task.FromResult<IActionResult>(Forbid());
        var correlation=Guid.TryParse(HttpContext.TraceIdentifier,out var id)?id:Guid.NewGuid();
        return Execute(org=>barcodes.SetAsync(org,branch,product,update,Request.Headers.Authorization.ToString(),new MenuMutationContext(subject,correlation),ct),ct);
    }
    [HttpPost("barcode-lookup"),RequestSizeLimit(4096)]
    public Task<IActionResult> Lookup(Guid branch,BarcodeLookupRequest request,CancellationToken ct)=>Execute(org=>barcodes.LookupAsync(org,branch,request.Barcode,Request.Headers.Authorization.ToString(),ct),ct);
    private async Task<IActionResult> Execute<T>(Func<Guid,Task<T>> action,CancellationToken ct)
    {
        if(!Guid.TryParse(Request.Headers[TenantContextHeaders.OrganizationId],out var org)||org==Guid.Empty
            ||Request.Headers[TenantContextHeaders.ApplicationCode]!="nexa_connect")return BadRequest();
        try{return Ok(await action(org));}
        catch(UnauthorizedAccessException){logger.LogWarning("Barcode authorization denied");return Forbid();}
        catch(KeyNotFoundException){return NotFound();}
        catch(BarcodeConflictException){logger.LogWarning("Barcode assignment conflict");return Conflict(new{title="Barcode belongs to another product or the version changed. Reload before saving."});}
        catch(ArgumentException){return BadRequest(new{title="Invalid barcode or version. Use up to 64 ASCII letters, digits, dots, underscores or hyphens."});}
        catch(Exception e)when(e is HttpRequestException or InvalidDataException||e is OperationCanceledException&&!ct.IsCancellationRequested)
        {logger.LogWarning("Barcode dependency unavailable");return StatusCode(503);}
    }
}
