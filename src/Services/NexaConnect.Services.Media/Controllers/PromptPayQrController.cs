using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexaConnect.Contracts.Platform;
using NexaConnect.Contracts.Media;
using NexaConnect.Services.Media.Application;
namespace NexaConnect.Services.Media.Controllers;

[ApiController,Authorize,ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
[Route("api/media/v1/customer/organizations/{org:guid}/branches/{branch:guid}")]
public sealed class PromptPayQrController(PromptPayQrDelivery delivery,PromptPayQrUploads uploads,ILogger<PromptPayQrController> logger):ControllerBase
{
    [HttpGet("promptpay-qr")]
    public Task<IActionResult> Get(Guid org,Guid branch,CancellationToken ct)=>Execute(async()=>Ok(await delivery.DeliverAsync(org,branch,Request.Headers.Authorization.ToString(),ct)),ct);
    [HttpGet("promptpay-qr/management-preview")]
    public Task<IActionResult> Preview(Guid org,Guid branch,CancellationToken ct)=>Execute(async()=>Ok(await delivery.DeliverAsync(org,branch,Request.Headers.Authorization.ToString(),ct,true)),ct);
    [HttpGet("promptpay-assets/{asset:guid}")]
    public Task<IActionResult> Ready(Guid org,Guid branch,Guid asset,CancellationToken ct)=>Execute(async()=>{
        await uploads.Require(org,branch,Request.Headers.Authorization.ToString(),ct);
        return await delivery.ReadyPngAsync(org,branch,asset,ct) is null?NotFound():Ok(new{ready=true});
    },ct);
    [HttpPost("promptpay-uploads"),RequestSizeLimit(4096)]
    public Task<IActionResult> Start(Guid org,Guid branch,StartPromptPayQrUploadV1 input,CancellationToken ct)=>Execute(async()=>{
        var session=await uploads.StartAsync(org,branch,input,User.FindFirst("sub")?.Value??"",Request.Headers.Authorization.ToString(),ct);
        return StatusCode(201,new PromptPayQrUploadSessionV1(org,branch,session.Asset.Id,session.Asset.ConcurrencyVersion,session.UploadUrl,session.ExpiresAtUtc));
    },ct);
    [HttpPost("promptpay-uploads/{asset:guid}/complete"),RequestSizeLimit(4096)]
    public Task<IActionResult> Complete(Guid org,Guid branch,Guid asset,CompletePromptPayQrUploadV1 input,CancellationToken ct)=>Execute(async()=>{
        var value=await uploads.CompleteAsync(org,branch,asset,input.ExpectedVersion,User.FindFirst("sub")?.Value??"",Request.Headers.Authorization.ToString(),ct);
        return Ok(new PromptPayQrUploadResultV1(org,branch,value.Id,value.ConcurrencyVersion,value.ProcessingStatus));
    },ct);
    private async Task<IActionResult> Execute(Func<Task<IActionResult>> work,CancellationToken ct)
    {
        try{return await work();}
        catch(UnauthorizedAccessException){logger.LogWarning("PromptPay image authorization denied");return Forbid();}
        catch(KeyNotFoundException){return NotFound();}
        catch(ArgumentException){return BadRequest(new{title="A valid branch PNG and current upload version are required."});}
        catch(MediaLifecycleConflictException){return Conflict(new{title="Upload changed, expired or failed safety checks."});}
        catch(MediaDependencyException){logger.LogWarning("PromptPay upload dependency unavailable");return StatusCode(503);}
        catch(Exception e)when(e is HttpRequestException or InvalidDataException or System.Text.Json.JsonException||e is OperationCanceledException&&!ct.IsCancellationRequested)
        {logger.LogWarning("PromptPay image dependency unavailable");return StatusCode(503);}
    }
}
