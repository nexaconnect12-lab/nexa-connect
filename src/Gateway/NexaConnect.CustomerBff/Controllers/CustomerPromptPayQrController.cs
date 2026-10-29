using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using NexaConnect.Contracts.Media;
using NexaConnect.CustomerBff.Application.PromptPay;
using NexaConnect.Infrastructure.Authentication;
namespace NexaConnect.CustomerBff.Controllers;

[ApiController,Authorize(Policy="CustomerSession"),ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
[Route("bff/customer/promptpay-qr")]
public sealed class CustomerPromptPayQrController(TenantSelectionCookie cookie,BffAccessTokenService tokens,IConfiguration configuration,CustomerPromptPayQrService service,ILogger<CustomerPromptPayQrController> logger):ControllerBase
{
    [HttpGet("csrf")]public IActionResult Csrf([FromServices]IAntiforgery antiforgery)=>Ok(new{requestToken=antiforgery.GetAndStoreTokens(HttpContext).RequestToken});
    [HttpGet("branches/{branch:guid}")]public Task<IActionResult> Read(Guid branch,CancellationToken ct)=>Forward(branch,null,false,ct);
    [HttpGet("branches/{branch:guid}/image")]public Task<IActionResult> Image(Guid branch,CancellationToken ct)=>Forward(branch,null,true,ct);
    [HttpPut("branches/{branch:guid}"),ValidateAntiForgeryToken,RequestSizeLimit(4096)]
    public Task<IActionResult> Save(Guid branch,UpdateBranchPromptPayQrV1 update,CancellationToken ct)=>Forward(branch,update,false,ct);
    [HttpPost("branches/{branch:guid}/uploads"),ValidateAntiForgeryToken,RequestSizeLimit(4096)]
    public Task<IActionResult> Start(Guid branch,StartPromptPayQrUploadV1 input,CancellationToken ct)=>Forward(branch,null,false,ct,start:input);
    [HttpPost("branches/{branch:guid}/uploads/{asset:guid}/complete"),ValidateAntiForgeryToken,RequestSizeLimit(4096)]
    public Task<IActionResult> Complete(Guid branch,Guid asset,CompletePromptPayQrUploadV1 input,CancellationToken ct)=>Forward(branch,null,false,ct,complete:input,asset:asset);
    private async Task<IActionResult> Forward(Guid branch,UpdateBranchPromptPayQrV1? update,bool image,CancellationToken ct,StartPromptPayQrUploadV1? start=null,CompletePromptPayQrUploadV1? complete=null,Guid? asset=null)
    {
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromSeconds(20));ct=deadline.Token;
        var tenant=cookie.Unprotect(Request.Cookies["__Host-nexa-customer-tenant"]);
        if(tenant is null||tenant.SubjectId!=User.FindFirstValue("sub")){logger.LogWarning("PromptPay BFF tenant denied");return Unauthorized();}
        try{
            var settings=configuration.GetRequiredSection("Bff");
            var token=await tokens.GetValidAccessTokenAsync(HttpContext,"CustomerCookie",settings["Authority"]!,settings["ClientId"]!,settings["ClientSecret"]!,ct);
            if(string.IsNullOrWhiteSpace(token))return Unauthorized();
            using var response=start is not null||complete is not null?await service.UploadAsync(tenant,branch,asset,start,complete,token,ct):await service.ExecuteAsync(tenant,branch,update,image,token,ct);
            if(!response.IsSuccessStatusCode){logger.LogWarning("PromptPay BFF boundary rejected {StatusCode}",(int)response.StatusCode);return StatusCode((int)response.StatusCode,new{title="PromptPay configuration rejected. Reload before retrying."});}
            await response.Content.LoadIntoBufferAsync(image?3_000_000:16384,ct);return Content(await response.Content.ReadAsStringAsync(ct),"application/json");
        }catch(Exception e)when(e is HttpRequestException or InvalidDataException or System.Text.Json.JsonException||e is OperationCanceledException&&!HttpContext.RequestAborted.IsCancellationRequested)
        {logger.LogWarning("PromptPay BFF dependency unavailable");return StatusCode(503);}
    }
}
