using System.Security.Claims;using System.Security.Cryptography;using System.Text;using Microsoft.AspNetCore.Mvc;using NexaConnect.Contracts.Platform;using NexaConnect.Services.Kitchen.Application;using NexaConnect.Services.Kitchen.Application.Tenant;using NexaConnect.Services.Kitchen.Domain;
namespace NexaConnect.Services.Kitchen.Controllers;
[ApiController][Route("api/kitchen/v1")]
public sealed class KitchenTicketsController(IKitchenTicketStore tickets,KitchenQueueService queue,ILogger<KitchenTicketsController> logger):ControllerBase
{
 [HttpPost("tickets")]
 public async Task<ActionResult<KitchenTicket>> Create(CreateKitchenTicket command,CancellationToken token){if(!IsOrderWorkload())return Forbid();if(!TryOrganization(out Guid organization))return BadRequest(new{error="A valid organization context is required."});try{var ticket=await tickets.CreateAsync(organization,command,Context(),token);return CreatedAtAction(nameof(Get),new{branchId=ticket.BranchId,ticketId=ticket.TicketId},ticket);}catch(ArgumentException e){return BadRequest(new{error=e.Message});}catch(KitchenConflictException e){return Conflict(new{error=e.Message});}}
 [HttpPost("tickets/{orderId:guid}/cancel")]
 public async Task<IActionResult> Cancel(Guid orderId,[FromQuery]Guid branchId,CancellationToken token){if(!IsOrderWorkload())return Forbid();if(!TryOrganization(out Guid organization)||branchId==Guid.Empty)return BadRequest(new{error="Organization and branch context are required."});try{return await tickets.CancelAsync(organization,branchId,orderId,Context(),token)?NoContent():NotFound();}catch(KitchenConflictException){logger.LogWarning("Kitchen compensation blocked by terminal preparation");return Conflict(new{error="Completed preparation cannot be cancelled. Operator investigation is required."});}}
 [HttpGet("branches/{branchId:guid}/tickets/{ticketId:guid}")]
 public Task<ActionResult<KitchenTicket>> Get(Guid branchId,Guid ticketId,CancellationToken token)=>OperatorResult(()=>queue.GetAsync(OperatorContext(branchId),ticketId,token),token);
 [HttpPost("branches/{branchId:guid}/tickets/{ticketId:guid}/transitions")]
 [RequestSizeLimit(4096)]
 public Task<ActionResult<KitchenTicket>> Transition(Guid branchId,Guid ticketId,TransitionKitchenTicket command,CancellationToken token)=>OperatorResult(()=>queue.TransitionAsync(OperatorContext(branchId),ticketId,command,Context(),token),token);
 [HttpGet("branches/{branchId:guid}/tickets")]
 public Task<ActionResult<KitchenQueuePage>> Queue(Guid branchId,[FromQuery]string? station,[FromQuery]int limit=50,[FromQuery]string? cursor=null,CancellationToken token=default)=>OperatorResult(()=>queue.ListAsync(OperatorContext(branchId),station,limit,cursor,token),token);
 private KitchenOperatorContext OperatorContext(Guid branchId)
 {
  TryOrganization(out Guid organization);
  return new(organization,branchId,Request.Headers[TenantContextHeaders.ApplicationCode].ToString(),Request.Headers.Authorization.ToString(),User.FindFirstValue("sub"),IsOrderWorkload());
 }
 private async Task<ActionResult<T>> OperatorResult<T>(Func<Task<T>> operation,CancellationToken ct)
 {
  Response.Headers.CacheControl="no-store";
  try{return Ok(await operation());}
  catch(UnauthorizedAccessException){logger.LogWarning("Kitchen operator authorization denied");return Forbid();}
  catch(KeyNotFoundException){return NotFound();}
  catch(ArgumentException){return BadRequest(new{error="Invalid Kitchen request."});}
  catch(KitchenConflictException){logger.LogInformation("Kitchen operator concurrency or lifecycle conflict");return Conflict(new{error="Ticket changed or transition is not allowed. Refresh before continuing."});}
  catch(OperationCanceledException)when(!ct.IsCancellationRequested){logger.LogWarning("Kitchen operator dependency timed out");return StatusCode(503);}
  catch(Exception e)when(e is HttpRequestException or Npgsql.NpgsqlException){logger.LogWarning("Kitchen operator dependency unavailable");return StatusCode(503);}
 }
 private bool IsOrderWorkload()=>string.Equals(User.FindFirstValue("azp"),"nexaconnect-order-service",StringComparison.Ordinal);
 private bool TryOrganization(out Guid id)=>Guid.TryParse(Request.Headers[TenantContextHeaders.OrganizationId],out id);
 private KitchenMutationContext Context(){string correlation=HttpContext.TraceIdentifier;Guid eventCorrelation=Guid.TryParse(correlation,out Guid id)?id:new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(correlation))[..16]);return new(IsOrderWorkload()?User.FindFirstValue("azp")??"nexaconnect-order-service":User.FindFirstValue("sub")??"kitchen-operator",eventCorrelation,correlation);}
}
