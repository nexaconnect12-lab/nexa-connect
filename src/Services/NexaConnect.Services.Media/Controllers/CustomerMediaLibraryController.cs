using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexaConnect.Services.Media.Application;

namespace NexaConnect.Services.Media.Controllers;

[ApiController,Authorize(Roles="customer-owner,customer-admin,customer-user,customer-manager,customer-viewer")]
[Route("api/media/v1/customer/organizations/{organization:guid}/library")]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class CustomerMediaLibraryController(MediaLibrary library,ILogger<CustomerMediaLibraryController> logger):ControllerBase
{
    [HttpGet] public Task<IActionResult> Browse(Guid organization,[FromQuery]string search="",[FromQuery]int page=1,[FromQuery]bool readyOnly=false,CancellationToken ct=default) =>
        Run(async token=>Ok(await library.BrowseAsync(organization,new(search,page,readyOnly),Request.Headers.Authorization.ToString(),token)),ct);
    [HttpGet("assets/{asset:guid}/thumbnail")] public Task<IActionResult> Thumbnail(Guid organization,Guid asset,CancellationToken ct)=>Run(async token=>
    {
        var png=await library.ThumbnailAsync(organization,asset,Request.Headers.Authorization.ToString(),token);
        Response.Headers.XContentTypeOptions="nosniff";return png is null?NotFound():File(png,"image/png");
    },ct);
    private async Task<IActionResult> Run(Func<CancellationToken,Task<IActionResult>> action,CancellationToken ct)
    {
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromSeconds(12));
        try{return await action(deadline.Token);}
        catch(UnauthorizedAccessException){logger.LogWarning("Media library authorization denied");return Forbid();}
        catch(ArgumentException){return BadRequest();}
        catch(Exception e)when(e is Npgsql.NpgsqlException or HttpRequestException or IOException or InvalidDataException or InvalidOperationException or Amazon.S3.AmazonS3Exception || e is OperationCanceledException&&!ct.IsCancellationRequested)
        {logger.LogWarning("Media library dependency unavailable {Category}",e.GetType().Name);return StatusCode(503);}
    }
}
