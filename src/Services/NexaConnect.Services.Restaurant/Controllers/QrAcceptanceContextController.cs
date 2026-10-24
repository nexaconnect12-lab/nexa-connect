using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexaConnect.Infrastructure.Authentication;
using NexaConnect.Contracts.RestaurantQr;
using NexaConnect.Services.Restaurant.Application.Qr;
using NexaConnect.Services.Restaurant.Application.Configuration;
namespace NexaConnect.Services.Restaurant.Controllers;
[ApiController,Authorize(Policy=NexaAuthorizationPolicies.ServiceWorkload)]
[Route("api/restaurant/v1/internal/organizations/{organization:guid}/branches/{branch:guid}/qr-visitors/{visitor:guid}/entries/{entry:guid}")]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class QrAcceptanceContextController(QrEntryService service,BranchProductConfigurationService configuration,IConfiguration settings,ILogger<QrAcceptanceContextController> logger):ControllerBase
{
    [HttpGet]public async Task<IActionResult> Get(Guid organization,Guid branch,Guid visitor,Guid entry,CancellationToken ct)
    {
        if(!settings.GetValue<bool>("QrEntry:Enabled"))return NotFound();
        if(User.FindFirst("azp")?.Value!="nexaconnect-order-service"){logger.LogWarning("QR acceptance context workload denied");return Forbid();}
        try
        {
            var context=await service.VisitorAsync(organization,branch,visitor,entry,ct);
            if(context is null)return NotFound();
            var policy=await configuration.GetPricingAsync(organization,branch,ct);
            return policy is null||policy.RestaurantId!=context.RestaurantId?NotFound():Ok(new QrAcceptanceContext(context,policy.DineInEnabled));
        }
        catch(Exception e)when(e is Npgsql.NpgsqlException or HttpRequestException){logger.LogWarning("QR acceptance context unavailable");return StatusCode(503);}
    }
}
