using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexaConnect.Contracts.Media;
using NexaConnect.Infrastructure.Authentication;
using NexaConnect.Services.Authorization.Application.Decisions;

namespace NexaConnect.Services.Authorization.Controllers;

[ApiController,Authorize(Policy=NexaAuthorizationPolicies.GalleryMediaWorkload)]
[Route("api/authorization/v1/internal/gallery-access")]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class GalleryAccessController(GalleryPermission permission,ILogger<GalleryAccessController> logger):ControllerBase
{
    [HttpPost,RequestSizeLimit(4096)]
    public async Task<IActionResult> Check(GalleryPermissionQuery query,CancellationToken ct)
    {
        var result=await permission.CheckAsync(query,ct);
        if(!result.Granted)logger.LogWarning("Gallery permission denied");
        return Ok(result);
    }
}
