using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexaConnect.Contracts.Media;
using NexaConnect.Infrastructure.Authentication;
using NexaConnect.Services.Restaurant.Application.Configuration;

namespace NexaConnect.Services.Restaurant.Controllers;

[ApiController,Authorize,ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class BranchPromptPayQrController(BranchPromptPayQrService service,ILogger<BranchPromptPayQrController> logger):ControllerBase
{
    [HttpGet("api/restaurant/v1/customer/organizations/{org:guid}/branches/{branch:guid}/promptpay-qr")]
    public Task<IActionResult> Read(Guid org,Guid branch,CancellationToken ct)=>Execute(()=>service.ReadAsync(org,branch,Request.Headers.Authorization.ToString(),ct));
    [HttpGet("api/restaurant/v1/customer/organizations/{org:guid}/branches/{branch:guid}/promptpay-qr/manage-access")]
    public Task<IActionResult> Access(Guid org,Guid branch,CancellationToken ct)=>Execute(async()=>await service.ManageAccessAsync(org,branch,Request.Headers.Authorization.ToString(),ct));
    [HttpPut("api/restaurant/v1/customer/organizations/{org:guid}/branches/{branch:guid}/promptpay-qr"),RequestSizeLimit(4096)]
    public Task<IActionResult> Save(Guid org,Guid branch,UpdateBranchPromptPayQrV1 update,CancellationToken ct)=>
        Execute(()=>service.SaveAsync(org,branch,update,User.FindFirst("sub")?.Value??"",Request.Headers.Authorization.ToString(),ct),true);
    [HttpGet("api/restaurant/v1/internal/organizations/{org:guid}/branches/{branch:guid}/promptpay-owner"),Authorize(Policy=NexaAuthorizationPolicies.GalleryMediaWorkload)]
    public async Task<IActionResult> Owner(Guid org,Guid branch,CancellationToken ct)=>await service.OwnerExistsAsync(org,branch,ct)?Ok(new{exists=true}):NotFound();
    private async Task<IActionResult> Execute(Func<Task<BranchPromptPayQrV1?>> work,bool write=false)
    {
        try{var value=await work();return value is null?write?Conflict():NotFound():Ok(value);}
        catch(UnauthorizedAccessException){logger.LogWarning("PromptPay configuration authorization denied");return Forbid();}
        catch(KeyNotFoundException){return NotFound();}
        catch(ArgumentException){return BadRequest(new{title="Current version, explicit approval and a ready branch PNG are required."});}
        catch(Exception e)when(e is HttpRequestException||e is OperationCanceledException&&!HttpContext.RequestAborted.IsCancellationRequested)
        {logger.LogWarning("PromptPay configuration dependency unavailable");return StatusCode(503);}
    }
}
