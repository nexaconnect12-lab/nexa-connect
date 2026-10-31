using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexaConnect.Contracts.Catalog;
using NexaConnect.Contracts.Platform;
using NexaConnect.Infrastructure.Authentication;
using NexaConnect.Services.Catalog.Application.Menu;
using NexaConnect.Services.Catalog.Application.MenuPhotos;
using NexaConnect.Services.Catalog.Domain.Photos;
namespace NexaConnect.Services.Catalog.Controllers;

[ApiController,Authorize,ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
[Route("api/catalog/v1/customer/branches/{branch:guid}/products/{product:guid}")]
public sealed class MenuPhotosController(MenuPhotos app,ILogger<MenuPhotosController> logger):ControllerBase
{
    [HttpGet("photo-selection")]public Task<IActionResult> Get(Guid branch,Guid product,CancellationToken ct)=>Run((org,bearer,t)=>Read(org,branch,product,bearer,t),ct);
    private async Task<IActionResult> Read(Guid org,Guid branch,Guid product,string bearer,CancellationToken ct)=>Ok(await app.ReadAsync(org,branch,product,bearer,ct));
    [HttpPut("photo-selection"),RequestSizeLimit(4096)]public Task<IActionResult> Save(Guid branch,Guid product,SetMenuPhoto input,CancellationToken ct)=>Run(async(org,bearer,t)=>Ok(await app.SetAsync(org,branch,product,input,bearer,new MenuMutationContext(User.FindFirst("sub")?.Value??"",Guid.TryParse(HttpContext.TraceIdentifier,out var id)?id:Guid.NewGuid()),t)),ct);
    [HttpGet("photo")]public Task<IActionResult> Photo(Guid branch,Guid product,CancellationToken ct)=>Run(async(org,bearer,t)=>{
        var png=await app.DeliverAsync(org,branch,product,bearer,t);Response.Headers.XContentTypeOptions="nosniff";return png is null?NotFound():File(png,"image/png");},ct);
    [HttpGet("photo-preview/{asset:guid}")]public Task<IActionResult> Preview(Guid branch,Guid product,Guid asset,CancellationToken ct)=>Run(async(org,bearer,t)=>{
        var png=await app.PreviewAsync(org,branch,product,asset,bearer,t);Response.Headers.XContentTypeOptions="nosniff";return png is null?NotFound():File(png,"image/png");},ct);
    private async Task<IActionResult> Run(Func<Guid,string,CancellationToken,Task<IActionResult>> work,CancellationToken ct)
    {
        if(!Guid.TryParse(Request.Headers[TenantContextHeaders.OrganizationId],out var org)||org==Guid.Empty||Request.Headers[TenantContextHeaders.ApplicationCode]!="nexa_connect")return BadRequest();
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromSeconds(15));
        try{return await work(org,Request.Headers.Authorization.ToString(),deadline.Token);}
        catch(UnauthorizedAccessException){logger.LogWarning("Menu photo authorization denied");return Forbid();}
        catch(KeyNotFoundException){return NotFound();}
        catch(MenuPhotoConflictException){return Conflict(new{code="menu_photo_changed"});}
        catch(ArgumentException){return BadRequest(new{code="invalid_or_unready_menu_photo"});}
        catch(Exception e)when(e is HttpRequestException or Npgsql.NpgsqlException or InvalidOperationException or System.Text.Json.JsonException or InvalidDataException||e is OperationCanceledException&&!ct.IsCancellationRequested){logger.LogWarning("Menu photo dependency unavailable {Category}",e.GetType().Name);return StatusCode(503);}
    }
}
[ApiController,Authorize(Policy=NexaAuthorizationPolicies.ServiceWorkload),ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
[Route("api/catalog/v1/internal/organizations/{org:guid}/branches/{branch:guid}/products/{product:guid}/photo")]
public sealed class InternalMenuPhotosController(MenuPhotos app,ILogger<InternalMenuPhotosController> logger):ControllerBase
{
    [HttpGet]public async Task<IActionResult> Get(Guid org,Guid branch,Guid product,CancellationToken ct)
    {
        if(User.FindFirst("azp")?.Value!="nexaconnect-order-service"){logger.LogWarning("Menu photo Catalog workload denied");return Forbid();}
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromSeconds(12));
        try{var png=await app.DeliverAsync(org,branch,product,null,deadline.Token);Response.Headers.XContentTypeOptions="nosniff";return png is null?NotFound():File(png,"image/png");}
        catch(KeyNotFoundException){return NotFound();}
        catch(ArgumentException){return BadRequest();}
        catch(Exception e)when(e is HttpRequestException or Npgsql.NpgsqlException or InvalidOperationException or System.Text.Json.JsonException or InvalidDataException||e is OperationCanceledException&&!ct.IsCancellationRequested){logger.LogWarning("Menu photo Catalog unavailable {Category}",e.GetType().Name);return StatusCode(503);}
    }
}
