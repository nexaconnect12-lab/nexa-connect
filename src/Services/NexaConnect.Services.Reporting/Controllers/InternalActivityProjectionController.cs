using Microsoft.AspNetCore.Authorization;using Microsoft.AspNetCore.Mvc;using NexaConnect.Contracts.IntegrationEvents;using NexaConnect.Services.Reporting.Application;
namespace NexaConnect.Services.Reporting.Controllers;
[ApiController,Authorize][Route("api/reporting/v1/internal/activity-projection")]
public sealed class InternalActivityProjectionController(ActivityService service,IConfiguration configuration,ILogger<InternalActivityProjectionController> logger):ControllerBase
{
 public sealed record ProjectionRequest(PlatformAuditEventV1 Event);
 [HttpPost]public async Task<IActionResult> Project(ProjectionRequest request,CancellationToken c){string? client=User.FindFirst("azp")?.Value;string? source=string.IsNullOrWhiteSpace(client)?null:configuration[$"ActivityProjection:Clients:{client}:SourceService"];if(string.IsNullOrWhiteSpace(source)){logger.LogWarning("Activity projection client denied for client {ClientId}",client);return Forbid();}try{bool inserted=await service.ProjectAsync(new(request.Event,"nexa_connect",source),c);logger.LogInformation("Activity projection event accepted for event {EventId}, source {SourceService}, inserted {Inserted}",request.Event.EventId,source,inserted);return Ok(new{inserted});}catch(ArgumentException e){return BadRequest(new ProblemDetails{Title=e.Message,Status=400});}catch(Exception e){logger.LogError(e,"Activity projection failed for event {EventId}",request.Event.EventId);throw;}}
}
