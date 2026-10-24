using System.Net.Http.Headers;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexaConnect.Contracts.Reporting;
using NexaConnect.Services.POS.Application.DayClose;
using NexaConnect.Services.POS.Application.Shifts;
using NexaConnect.Services.POS.Domain.DayClose;
namespace NexaConnect.Services.POS.Controllers;
public sealed record CorrectionRequest(Guid BranchId,DateOnly BusinessDate,Guid WorkId,LateCashCorrectionCommand? Command=null);
[ApiController,Authorize(Roles="customer-owner,customer-admin,customer-manager,customer-viewer"),ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
[Route("api/pos/v1/customer/organizations/{organizationId:guid}/late-cash-corrections")]
public sealed class LateCashCorrectionsController(LateCashCorrections application,ILogger<LateCashCorrectionsController> logger):ControllerBase
{
 [HttpGet]public Task<IActionResult> Preview(Guid organizationId,[FromQuery]Guid branchId,[FromQuery]DateOnly businessDate,[FromQuery]Guid workId,CancellationToken ct)=>Run((user,token)=>application.PreviewAsync(organizationId,branchId,businessDate,workId,user,token),ct);
 [HttpPost,RequestSizeLimit(4096)]public Task<IActionResult> Post(Guid organizationId,CorrectionRequest request,CancellationToken ct)
 {if(request.Command is null||request.Command.WorkId!=request.WorkId)return Task.FromResult<IActionResult>(BadRequest());return Run((user,token)=>application.PostAsync(organizationId,request.BranchId,request.BusinessDate,request.Command,user,Guid.NewGuid(),token),ct);}
 private async Task<IActionResult> Run(Func<PosUserContext,CancellationToken,Task<LateCashCorrectionView>> action,CancellationToken ct)
 {
  var subject=User.FindFirstValue("sub")??User.FindFirstValue(ClaimTypes.NameIdentifier);
  if(subject is null||!AuthenticationHeaderValue.TryParse(Request.Headers.Authorization,out var header)||!header.Scheme.Equals("Bearer",StringComparison.OrdinalIgnoreCase)||string.IsNullOrWhiteSpace(header.Parameter))return Unauthorized();
  using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromSeconds(25));
  try{return Ok(await action(new(subject,header.Parameter),deadline.Token));}catch(ArgumentException){return BadRequest();}catch(UnauthorizedAccessException){logger.LogWarning("Cash correction authorization denied");return Forbid();}
  catch(DayCloseConflictException){logger.LogWarning("Cash correction conflict");return Conflict(new{code="cash_correction_conflict"});}
  catch(Exception e)when(!ct.IsCancellationRequested){logger.LogWarning("Cash correction unavailable; category {Category}",e.GetType().Name);return StatusCode(503);}
 }
}
