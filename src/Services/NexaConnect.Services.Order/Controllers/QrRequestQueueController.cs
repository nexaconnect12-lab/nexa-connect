using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexaConnect.Contracts.Platform;
using NexaConnect.Contracts.RestaurantQr;
using NexaConnect.Services.Order.Application.Orders;
using NexaConnect.Services.Order.Domain.Qr;
namespace NexaConnect.Services.Order.Controllers;

[ApiController,Authorize(Roles="customer-owner,customer-admin,customer-manager,customer-user,customer-viewer")]
[Route("api/order/v1/customer/organizations/{organization:guid}/branches/{branch:guid}/qr-requests")]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class QrRequestQueueController(QrRequestService service,IConfiguration configuration,ILogger<QrRequestQueueController> logger):ControllerBase
{
    [HttpGet] public Task<IActionResult> Queue(Guid organization,Guid branch,[FromQuery]string state="pending",CancellationToken ct=default)=>Run(organization,async token=>Ok(await service.QueueAsync(organization,branch,state,Request.Headers.Authorization.ToString(),token)),ct);
    [HttpPost("{id:guid}/reject"),RequestSizeLimit(1024)] public Task<IActionResult> Reject(Guid organization,Guid branch,Guid id,QrRejection input,CancellationToken ct)=>Run(organization,async token=>
    {
        var value=await service.RejectAsync(organization,branch,id,input,User.FindFirstValue("sub")??"",Request.Headers.Authorization.ToString(),Guid.TryParse(HttpContext.TraceIdentifier,out var correlation)?correlation:Guid.NewGuid(),token);
        return value is null?NotFound():Ok(value);
    },ct);
    private async Task<IActionResult> Run(Guid organization,Func<CancellationToken,Task<IActionResult>> action,CancellationToken ct)
    {
        if(!configuration.GetValue<bool>("QrRequests:Enabled"))return NotFound();
        if(Request.Headers[TenantContextHeaders.OrganizationId]!=organization.ToString("D")||Request.Headers[TenantContextHeaders.ApplicationCode]!="nexa_connect")return Forbid();
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromSeconds(12));
        try{return await action(deadline.Token);}
        catch(UnauthorizedAccessException){logger.LogWarning("QR review authorization denied");return Forbid();}
        catch(GuestRequestConflictException){logger.LogWarning("QR review version or state conflict");return Conflict();}
        catch(ArgumentException){return BadRequest();}
        catch(Exception e)when(e is Npgsql.NpgsqlException or HttpRequestException or System.Text.Json.JsonException||e is OperationCanceledException&&!ct.IsCancellationRequested)
        {logger.LogWarning("QR review dependency unavailable");return StatusCode(503);}
    }
}
