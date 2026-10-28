using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexaConnect.Contracts.Media;
using NexaConnect.Infrastructure.Authentication;
using NexaConnect.Services.Restaurant.Application.Configuration;

namespace NexaConnect.Services.Restaurant.Controllers;

[ApiController,Authorize(Policy=NexaAuthorizationPolicies.GalleryMediaWorkload)]
[Route("api/restaurant/v1/internal/organizations/{org:guid}/branches/{branch:guid}/screensaver-gallery")]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class GalleryReaderController(GalleryReaderSelection selection,ILogger<GalleryReaderController> logger):ControllerBase
{
    [HttpPost,RequestSizeLimit(4096)]
    public async Task<IActionResult> Read(Guid org,Guid branch,GalleryViewer viewer,CancellationToken ct)
    {
        try{return Ok(await selection.ReadAsync(org,branch,viewer.Subject,Request.Headers.Authorization.ToString(),ct));}
        catch(UnauthorizedAccessException){logger.LogWarning("Gallery reader scope denied");return Forbid();}
        catch(KeyNotFoundException){return NotFound();}
        catch(Exception e)when(e is HttpRequestException or InvalidDataException or System.Text.Json.JsonException || e is OperationCanceledException && !ct.IsCancellationRequested)
        {logger.LogWarning("Gallery reader authority unavailable");return StatusCode(503);}
    }
}
