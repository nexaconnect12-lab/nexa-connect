using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexaConnect.Infrastructure.Authentication;
using NexaConnect.Infrastructure.Http;
using NexaConnect.Services.Media.Application;

namespace NexaConnect.Services.Media.Controllers;

[ApiController,ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class GalleryReadersController(GalleryReaders readers,BoundedRequestGate gate,ILogger<GalleryReadersController> logger):ControllerBase
{
    [Authorize(Roles="customer-owner,customer-admin,customer-user,customer-manager,customer-viewer")]
    [HttpPost("api/media/v1/customer/organizations/{org:guid}/branches/{branch:guid}/screensaver-gallery/readers")]
    public async Task<IActionResult> Issue(Guid org,Guid branch,CancellationToken ct)
    {
        var subject=User.FindFirst(NexaAuthenticationDefaults.SubjectClaim)?.Value;
        if(string.IsNullOrWhiteSpace(subject))return Forbid();
        return await Boundary(()=>readers.IssueAsync(org,branch,subject,Request.Headers.Authorization.ToString(),ct),ct);
    }
    [AllowAnonymous]
    [HttpGet("api/media/v1/gallery-readers/organizations/{org:guid}/branches/{branch:guid}")]
    public async Task<IActionResult> Read(Guid org,Guid branch,CancellationToken ct)
    {
        if(!gate.Admit(HttpContext.Connection.RemoteIpAddress?.ToString()??"unknown"))return StatusCode(429);
        // This capability is accepted only here, never as a Bearer token or URL parameter.
        var values=Request.Headers["X-Nexa-Gallery-Reader"];
        if(values.Count!=1||Request.QueryString.HasValue)return Unauthorized();
        return await Boundary(()=>readers.ReadAsync(org,branch,values.ToString(),ct),ct);
    }
    private async Task<IActionResult> Boundary<T>(Func<Task<T>> action,CancellationToken ct)
    {
        try{return Ok(await action());}
        catch(UnauthorizedAccessException){logger.LogWarning("Gallery reader authorization denied");return StatusCode(403);}
        catch(KeyNotFoundException){return NotFound();}
        catch(Exception e)when(e is HttpRequestException or InvalidDataException or System.Text.Json.JsonException || e is OperationCanceledException && !ct.IsCancellationRequested)
        {logger.LogWarning("Gallery reader dependency unavailable");return StatusCode(503);}
    }
}
