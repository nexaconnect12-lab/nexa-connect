using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexaConnect.Contracts.Media;
using NexaConnect.Services.Restaurant.Application.Configuration;

namespace NexaConnect.Services.Restaurant.Controllers;

[ApiController,Authorize(Roles="customer-owner,customer-admin,customer-user,customer-manager,customer-viewer")]
[Route("api/restaurant/v1/customer/organizations/{org:guid}/branches/{branch:guid}/screensaver-gallery")]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class BranchGalleryController(BranchGalleryService gallery, ILogger<BranchGalleryController> logger) : ControllerBase
{
    [HttpGet] public Task<IActionResult> Get(Guid org,Guid branch,CancellationToken ct) => Execute(()=>gallery.GetAsync(org,branch,Request.Headers.Authorization.ToString(),ct));
    [HttpPut,RequestSizeLimit(16384)] public Task<IActionResult> Save(Guid org,Guid branch,UpdateBranchGallery update,CancellationToken ct) =>
        Execute(()=>gallery.SaveAsync(org,branch,update,User.FindFirstValue("sub")??"",Request.Headers.Authorization.ToString(),ct),true);
    private async Task<IActionResult> Execute(Func<Task<BranchGallery?>> action,bool write=false)
    {
        try { var result=await action();return result is null ? write ? Conflict(new{title="Branch changed or unavailable. Reload before saving."}) : NotFound() : Ok(result); }
        catch(UnauthorizedAccessException){logger.LogWarning("Gallery configuration authorization denied");return Forbid();}
        catch(ArgumentException){return BadRequest(new{title="Gallery selection is invalid or media is not ready."});}
        catch(Exception e) when(e is HttpRequestException or TimeoutException || e is OperationCanceledException && !HttpContext.RequestAborted.IsCancellationRequested)
        {logger.LogWarning("Gallery configuration dependency unavailable");return StatusCode(503);}
    }
}
