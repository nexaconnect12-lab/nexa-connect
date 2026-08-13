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
public sealed class CustomerMediaController(MediaAssetQueries queries,IMediaCustomerAuthorizer authorizer,ILogger<CustomerMediaController> logger):ControllerBase
{
 [HttpGet] public async Task<IActionResult> List(Guid organizationId,CancellationToken c)
 {
  string? actor=User.FindFirstValue("sub")??User.FindFirstValue(ClaimTypes.NameIdentifier);bool granted=actor is not null&&await authorizer.IsGrantedAsync(organizationId,ProductPermissions.MediaAssetRead,Request.Headers.Authorization.ToString(),c);if(!granted){logger.LogWarning("Customer media authorization denied for organization {OrganizationId}, actor {ActorSubjectId}",organizationId,actor);return Forbid();}
  return Ok(await queries.ListAsync(organizationId,c));
 }
}
