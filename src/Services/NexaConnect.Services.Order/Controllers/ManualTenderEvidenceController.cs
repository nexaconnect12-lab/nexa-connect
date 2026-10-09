using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexaConnect.Services.Order.Application.ManualTenders;
namespace NexaConnect.Services.Order.Controllers;
[ApiController,Authorize(Roles="customer-owner,customer-admin,customer-manager,customer-viewer"),ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
[Route("api/order/v1/customer/manual-tender-evidence/{eventId:guid}")]
public sealed class ManualTenderEvidenceController(ManualTenderEvidence application,ILogger<ManualTenderEvidenceController> logger):ControllerBase
{
 [HttpGet]public async Task<IActionResult> Read(Guid eventId,[FromQuery]Guid organizationId,[FromQuery]Guid restaurantId,[FromQuery]Guid branchId,CancellationToken ct)
 {
  using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromSeconds(15));
  try{var result=await application.ReadAsync(organizationId,restaurantId,branchId,eventId,Request.Headers.Authorization.ToString(),deadline.Token);return result is null?NotFound():Ok(result);}
  catch(ArgumentException){return BadRequest();}catch(UnauthorizedAccessException){logger.LogWarning("Tender evidence authorization denied");return Forbid();}
  catch(Exception e)when(!ct.IsCancellationRequested){logger.LogWarning("Tender evidence unavailable; category {Category}",e.GetType().Name);return StatusCode(503);}
 }
}
