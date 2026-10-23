using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexaConnect.Contracts.Platform;
using NexaConnect.Services.Media.Application;

namespace NexaConnect.Services.Media.Controllers;

[ApiController,Authorize(Roles="customer-owner,customer-admin,customer-user,customer-manager,customer-viewer")]
[Route("api/media/v1/customer/organizations/{org:guid}")]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class BranchGalleryController(BranchGalleryDelivery delivery,IMediaCustomerAuthorizer authorizer,ILogger<BranchGalleryController> logger):ControllerBase
{
    [HttpGet("gallery-assets/{asset:guid}")]
    public async Task<IActionResult> Ready(Guid org,Guid asset,CancellationToken ct)
    {
        try {
            if(!await authorizer.IsGrantedAsync(org,ProductPermissions.MediaAssetRead,Request.Headers.Authorization.ToString(),ct))
            {logger.LogWarning("Gallery asset authorization denied");return Forbid();}
            return await delivery.ReadyAsync(org,asset,ct)?Ok(new{ready=true}):NotFound();
        }catch(Exception e) when(e is HttpRequestException or OperationCanceledException && !ct.IsCancellationRequested)
        {logger.LogWarning("Gallery asset dependency unavailable");return StatusCode(503);}
    }
    [HttpGet("branches/{branch:guid}/screensaver-gallery")]
    public async Task<IActionResult> Bundle(Guid org,Guid branch,CancellationToken ct)
    {
        try{return Ok(await delivery.DeliverAsync(org,branch,Request.Headers.Authorization.ToString(),ct));}
        catch(UnauthorizedAccessException){logger.LogWarning("Gallery delivery authorization denied");return Forbid();}
        catch(KeyNotFoundException){return NotFound();}
        catch(Exception e) when(e is HttpRequestException or InvalidDataException || e is OperationCanceledException && !ct.IsCancellationRequested)
        {logger.LogWarning("Gallery delivery unavailable");return StatusCode(503);}
    }
}
