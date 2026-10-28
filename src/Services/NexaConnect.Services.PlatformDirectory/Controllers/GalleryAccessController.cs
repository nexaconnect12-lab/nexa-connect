using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexaConnect.Contracts.Media;
using NexaConnect.Infrastructure.Authentication;
using NexaConnect.Services.PlatformDirectory.Application.Access;

namespace NexaConnect.Services.PlatformDirectory.Controllers;

[ApiController, Authorize(Policy=NexaAuthorizationPolicies.GalleryMediaWorkload)]
[Route("api/platform-directory/v1/internal/gallery-access")]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class GalleryAccessController(GalleryMembership membership, ILogger<GalleryAccessController> logger):ControllerBase
{
    [HttpPost,RequestSizeLimit(4096)]
    public async Task<IActionResult> Check(GalleryMembershipQuery query,CancellationToken ct)
    {
        var result=await membership.CheckAsync(query,ct);
        if(!result.Granted)logger.LogWarning("Gallery membership denied");
        return Ok(result);
    }
}
