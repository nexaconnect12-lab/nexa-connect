using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using NexaConnect.Infrastructure.Http;
namespace NexaConnect.CustomerBff.Controllers;
[ApiController,AllowAnonymous,ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
[Route("bff/guest/qr/menu/{product:guid}/photo")]
public sealed class QrMenuPhotosController(IHttpClientFactory clients,IOptions<QrGuestOptions> settings,[FromKeyedServices("menu-photos")]BoundedRequestGate gate,ILogger<QrMenuPhotosController> logger):ControllerBase
{
    [HttpGet]public async Task<IActionResult> Get(Guid product,CancellationToken ct)
    {
        if(!settings.Value.Enabled||!settings.Value.MenuEnabled)return NotFound();
        if(!Request.IsHttps||!string.Equals($"https://{Request.Host}",settings.Value.Origin,StringComparison.OrdinalIgnoreCase)){logger.LogWarning("QR menu photo origin denied");return StatusCode(403);}
        var session=Request.Cookies["__Host-nexa-qr-guest"];
        if(session?.Length!=43||!session.All(c=>char.IsAsciiLetterOrDigit(c)||c is '-' or '_'))return Unauthorized();
        if(!gate.Admit("photo:"+(HttpContext.Connection.RemoteIpAddress?.ToString()??"unknown"))){Response.Headers.RetryAfter="60";return StatusCode(429);}
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromSeconds(15));
        try{
            using var request=new HttpRequestMessage(HttpMethod.Get,$"api/order/v1/guest/qr/menu/{product:D}/photo");
            // Only the guest cookie capability is forwarded, never staff Authorization or tenant headers.
            request.Headers.Add("X-Nexa-Guest-Session",session);
            using var response=await clients.CreateClient("QrGuestOrder").SendAsync(request,HttpCompletionOption.ResponseHeadersRead,deadline.Token);
            if(!response.IsSuccessStatusCode){logger.LogWarning("QR menu photo boundary rejected {StatusCode}",(int)response.StatusCode);return StatusCode((int)response.StatusCode is 401 or 403 or 404 or 429?(int)response.StatusCode:503);}
            if(response.Content.Headers.ContentType?.MediaType!="image/png"||response.Content.Headers.ContentLength>524288)throw new InvalidDataException();
            await response.Content.LoadIntoBufferAsync(524288,deadline.Token);Response.Headers.XContentTypeOptions="nosniff";
            return File(await response.Content.ReadAsByteArrayAsync(deadline.Token),"image/png");
        }catch(Exception e)when(e is HttpRequestException or InvalidDataException||e is OperationCanceledException&&!ct.IsCancellationRequested){logger.LogWarning("QR menu photo dependency unavailable {Category}",e.GetType().Name);return StatusCode(503);}
    }
}
