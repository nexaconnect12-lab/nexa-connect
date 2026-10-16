using System.Net.Http.Headers;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexaConnect.Services.POS.Application.DayClose;
using NexaConnect.Services.POS.Application.Shifts;
using NexaConnect.Services.POS.Domain.DayClose;
namespace NexaConnect.Services.POS.Controllers;
[ApiController,Authorize(Roles="customer-owner,customer-admin,customer-manager,customer-viewer"),ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
[Route("api/pos/v1/customer/organizations/{organizationId:guid}/day-close-late-work")]
public sealed class DayLateWorkController(DayLateWork application,ILogger<DayLateWorkController> logger):ControllerBase
{
 [HttpGet]public Task<IActionResult> List(Guid organizationId,[FromQuery] Guid branchId,[FromQuery] DateOnly businessDate,[FromQuery]string source,[FromQuery]string? cursor=null,[FromQuery]int limit=25,CancellationToken ct=default)=>Run(organizationId,new(branchId,businessDate,source,Cursor:cursor,Limit:limit),"list",ct);
 [HttpGet("{id:guid}")]public Task<IActionResult> Detail(Guid organizationId,Guid id,[FromQuery]Guid branchId,[FromQuery]DateOnly businessDate,[FromQuery]string source,CancellationToken ct)=>Run(organizationId,new(branchId,businessDate,source,id),"detail",ct);
 [HttpPost,RequestSizeLimit(4096)]public Task<IActionResult> Review(Guid organizationId,DayLateWorkRequest input,CancellationToken ct)=>Run(organizationId,input,"review",ct);
 private async Task<IActionResult> Run(Guid organization,DayLateWorkRequest input,string action,CancellationToken ct)
 {
  var subject=User.FindFirstValue("sub")??User.FindFirstValue(ClaimTypes.NameIdentifier);
  if(subject is null||!AuthenticationHeaderValue.TryParse(Request.Headers.Authorization,out var header)||!header.Scheme.Equals("Bearer",StringComparison.OrdinalIgnoreCase)||string.IsNullOrWhiteSpace(header.Parameter))return Unauthorized();
  using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromSeconds(25));
  try{var result=await application.ExecuteAsync(organization,input,action,new(subject,header.Parameter),deadline.Token);return result is null?NotFound():Ok(result);}
  catch(ArgumentException){return BadRequest();}catch(UnauthorizedAccessException){logger.LogWarning("Late-work facade authorization denied");return Forbid();}
  catch(DayCloseConflictException){logger.LogWarning("Late-work facade conflict");return Conflict(new{code="late_review_conflict"});}
  catch(Exception e)when(!ct.IsCancellationRequested){logger.LogWarning("Late-work facade unavailable; category {Category}",e.GetType().Name);return StatusCode(503);}
 }
}
