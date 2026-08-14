using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexaConnect.Contracts.Platform;
using NexaConnect.Infrastructure.Authorization;
using Npgsql;
using NexaConnect.Services.Media.Application;

namespace NexaConnect.Services.Media.Controllers;

[ApiController,Authorize(Roles="customer-owner,customer-admin,customer-manager,customer-viewer")]
[Route("api/media/v1/customer/organizations/{organizationId:guid}/assets")]
public sealed class CustomerMediaController(MediaAssetQueries queries,MediaManagement management,IMediaCustomerAuthorizer authorizer,ILogger<CustomerMediaController> logger):ControllerBase
{
 [HttpGet] public async Task<IActionResult> List(Guid organizationId,CancellationToken c)
 {
  string? actor=User.FindFirstValue("sub")??User.FindFirstValue(ClaimTypes.NameIdentifier);bool granted=actor is not null&&await authorizer.IsGrantedAsync(organizationId,ProductPermissions.MediaAssetRead,Request.Headers.Authorization.ToString(),c);if(!granted){logger.LogWarning("Customer media authorization denied for organization {OrganizationId}, actor {ActorSubjectId}",organizationId,actor);return Forbid();}
  return Ok(await queries.ListAsync(organizationId,c));
 }
 [HttpPost("uploads")]public async Task<IActionResult>Start(Guid organizationId,StartMediaUploadCommand request,CancellationToken c){if(!await Granted(organizationId,ProductPermissions.MediaAssetManage,c))return Forbid();try{return StatusCode(201,await management.StartAsync(organizationId,request,Actor()!,c));}catch(ArgumentException e){return BadRequest(new ProblemDetails{Title=e.Message,Status=400});}catch(KeyNotFoundException){return NotFound();}catch(HttpRequestException e){logger.LogError(e,"Media owner validation failed for organization {OrganizationId}",organizationId);return StatusCode(503,new ProblemDetails{Title="Catalog owner validation is unavailable.",Status=503});}}
 [HttpPost("{id:guid}/complete")]public async Task<IActionResult>Complete(Guid organizationId,Guid id,VersionRequest request,CancellationToken c){if(!await Granted(organizationId,ProductPermissions.MediaAssetManage,c))return Forbid();try{return Ok(await management.CompleteAsync(organizationId,id,request.ExpectedVersion,Actor()!,c));}catch(ArgumentException e){return BadRequest(new ProblemDetails{Title=e.Message,Status=400});}catch(KeyNotFoundException){return NotFound();}catch(InvalidOperationException e){return Conflict(new ProblemDetails{Title=e.Message,Status=409});}}
 [HttpGet("{id:guid}/download")]public async Task<IActionResult>Download(Guid organizationId,Guid id,CancellationToken c){if(!await Granted(organizationId,ProductPermissions.MediaAssetRead,c))return Forbid();try{return Ok(await management.DownloadAsync(organizationId,id,c));}catch(KeyNotFoundException){return NotFound();}catch(InvalidOperationException e){return Conflict(new ProblemDetails{Title=e.Message,Status=409});}}
 [HttpDelete("{id:guid}")]public async Task<IActionResult>Delete(Guid organizationId,Guid id,[FromQuery]long expectedVersion,CancellationToken c){if(!await Granted(organizationId,ProductPermissions.MediaAssetManage,c))return Forbid();try{return Ok(await management.DeleteAsync(organizationId,id,expectedVersion,Actor()!,c));}catch(ArgumentException e){return BadRequest(new ProblemDetails{Title=e.Message,Status=400});}catch(KeyNotFoundException){return NotFound();}catch(InvalidOperationException e){return Conflict(new ProblemDetails{Title=e.Message,Status=409});}}
 async Task<bool>Granted(Guid org,string permission,CancellationToken c){string? actor=Actor();bool granted=actor is not null&&await authorizer.IsGrantedAsync(org,permission,Request.Headers.Authorization.ToString(),c);if(!granted)logger.LogWarning("Customer media authorization denied for organization {OrganizationId}, actor {ActorSubjectId}, permission {PermissionCode}",org,actor,permission);return granted;}
 string?Actor()=>User.FindFirstValue("sub")??User.FindFirstValue(ClaimTypes.NameIdentifier);public sealed record VersionRequest(long ExpectedVersion);
}
