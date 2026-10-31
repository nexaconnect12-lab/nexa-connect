using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexaConnect.Infrastructure.Authentication;
using NexaConnect.Services.Media.Application;
namespace NexaConnect.Services.Media.Controllers;
[ApiController,Authorize(Policy=NexaAuthorizationPolicies.ServiceWorkload),ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
[Route("api/media/v1/internal/organizations/{org:guid}/catalog-menu-images/{asset:guid}")]
public sealed class InternalMenuPhotosController(MenuPhotoDelivery delivery,ILogger<InternalMenuPhotosController> logger):ControllerBase
{
    [HttpGet]public async Task<IActionResult> Get(Guid org,Guid asset,CancellationToken ct)
    {
        if(User.FindFirst("azp")?.Value!="nexaconnect-catalog-service"){logger.LogWarning("Menu photo workload denied");return Forbid();}
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromSeconds(10));
        try{var png=await delivery.ReadAsync(org,asset,deadline.Token);Response.Headers.XContentTypeOptions="nosniff";return png is null?NotFound():File(png,"image/png");}
        catch(Exception e)when(e is HttpRequestException or Npgsql.NpgsqlException or InvalidDataException or InvalidOperationException or System.Text.Json.JsonException or Amazon.S3.AmazonS3Exception or IOException||e is OperationCanceledException&&!ct.IsCancellationRequested){logger.LogWarning("Menu photo media dependency unavailable {Category}",e.GetType().Name);return StatusCode(503);}
    }
}
