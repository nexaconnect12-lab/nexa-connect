using System.Net.Http.Headers;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexaConnect.Contracts.Reporting;
using NexaConnect.Infrastructure.Persistence;
using NexaConnect.Services.Payment.Application.Refunds;
namespace NexaConnect.Services.Payment.Controllers;
[ApiController,Authorize(Roles="customer-owner,customer-admin,customer-manager,customer-viewer"),ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
[Route("api/payment/v1/customer/late-work")]
public sealed class LateWorkController(PaymentLateWork application,ILogger<LateWorkController> logger):ControllerBase
{
 [HttpGet]public Task<IActionResult> List([FromQuery] EndOfDayWindow window,[FromQuery] Guid settlementId,[FromQuery] string? cursor=null,[FromQuery] int limit=25,CancellationToken ct=default)=>Run(async token=>Ok(await application.ListAsync(new(window,settlementId),cursor,limit,Request.Headers.Authorization.ToString(),token)),ct);
 [HttpGet("{id:guid}")]public Task<IActionResult> Read(Guid id,[FromQuery] EndOfDayWindow window,[FromQuery] Guid settlementId,CancellationToken ct)=>Run(async token=>await application.ReadAsync(new(window,settlementId),id,Request.Headers.Authorization.ToString(),token) is {} detail?Ok(detail):NotFound(),ct);
 [HttpPost("reviews"),RequestSizeLimit(4096)]public Task<IActionResult> Review(SourceLateReviewRequest request,CancellationToken ct)=>Run(async token=>Ok(await application.ReviewAsync(request.Scope,request.Command,Request.Headers.Authorization.ToString(),subject,token)),ct);
 private string subject=>User.FindFirstValue("sub")??User.FindFirstValue(ClaimTypes.NameIdentifier)??throw new UnauthorizedAccessException();
 private AuthenticationHeaderValue header=>AuthenticationHeaderValue.TryParse(Request.Headers.Authorization,out var value)&&value.Scheme.Equals("Bearer",StringComparison.OrdinalIgnoreCase)&&!string.IsNullOrWhiteSpace(value.Parameter)?value:throw new UnauthorizedAccessException();
 private async Task<IActionResult> Run(Func<CancellationToken,Task<IActionResult>> action,CancellationToken ct)
 {
  using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromSeconds(25));
  try{return await action(deadline.Token);}catch(ArgumentException){return BadRequest();}catch(UnauthorizedAccessException){logger.LogWarning("Late-work authorization denied");return Forbid();}
  catch(SnapshotOperationConflictException){logger.LogWarning("Late-work review conflict");return Conflict(new{code="late_review_conflict"});}
  catch(Exception e)when(!ct.IsCancellationRequested){logger.LogWarning("Late-work unavailable; category {Category}",e.GetType().Name);return StatusCode(503);}
 }
}
