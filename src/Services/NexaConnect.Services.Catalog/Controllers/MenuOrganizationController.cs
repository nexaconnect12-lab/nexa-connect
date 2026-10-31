using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexaConnect.Contracts.Catalog;
using NexaConnect.Contracts.Platform;
using NexaConnect.Infrastructure.Authentication;
using NexaConnect.Services.Catalog.Application.Menu;
using NexaConnect.Services.Catalog.Application.Organization;
using NexaConnect.Services.Catalog.Domain.Organization;
namespace NexaConnect.Services.Catalog.Controllers;
[ApiController,Authorize,ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
[Route("api/catalog/v1/customer/branches/{branch:guid}/menu-organization")]
public sealed class MenuOrganizationController(MenuOrganizationService app,ILogger<MenuOrganizationController> logger):ControllerBase
{
    [HttpGet]public Task<IActionResult> Read(Guid branch,CancellationToken ct)=>Run(branch,null,ct);
    [HttpPut,RequestSizeLimit(65536)]public Task<IActionResult> Save(Guid branch,UpdateMenuOrganization input,CancellationToken ct)=>Run(branch,input,ct);
    private async Task<IActionResult> Run(Guid branch,UpdateMenuOrganization? input,CancellationToken ct)
    {
        if(!Guid.TryParse(Request.Headers[TenantContextHeaders.OrganizationId],out var org)||org==Guid.Empty||Request.Headers[TenantContextHeaders.ApplicationCode]!="nexa_connect")return BadRequest();
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromSeconds(15));
        try{var bearer=Request.Headers.Authorization.ToString();return Ok(input is null?await app.ReadAsync(org,branch,bearer,deadline.Token):await app.SaveAsync(org,branch,input,bearer,new MenuMutationContext(User.FindFirst("sub")?.Value??"",Guid.TryParse(HttpContext.TraceIdentifier,out var id)?id:Guid.NewGuid()),deadline.Token));}
        catch(UnauthorizedAccessException){logger.LogWarning("Menu organization authorization denied");return Forbid();}
        catch(MenuOrganizationConflictException){return Conflict(new{code="menu_organization_changed"});}
        catch(ArgumentException){return BadRequest(new{code="invalid_menu_organization"});}
        catch(Exception e)when(e is HttpRequestException or Npgsql.NpgsqlException or InvalidOperationException or System.Text.Json.JsonException||e is OperationCanceledException&&!ct.IsCancellationRequested){logger.LogWarning("Menu organization unavailable {Category}",e.GetType().Name);return StatusCode(503);}
    }
}
[ApiController,Authorize(Policy=NexaAuthorizationPolicies.ServiceWorkload),ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
[Route("api/catalog/v1/internal/organizations/{org:guid}/branches/{branch:guid}/menu-organization")]
public sealed class InternalMenuOrganizationController(MenuOrganizationService app,ILogger<InternalMenuOrganizationController> logger):ControllerBase
{
    [HttpGet]public async Task<IActionResult> Read(Guid org,Guid branch,CancellationToken ct)
    {
        if(User.FindFirst("azp")?.Value!="nexaconnect-order-service"){logger.LogWarning("Menu organization workload denied");return Forbid();}
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromSeconds(12));
        try{return Ok(await app.ReadAsync(org,branch,null,deadline.Token));}
        catch(ArgumentException){return BadRequest();}
        catch(Exception e)when(e is HttpRequestException or Npgsql.NpgsqlException or System.Text.Json.JsonException||e is OperationCanceledException&&!ct.IsCancellationRequested){logger.LogWarning("Menu organization dependency unavailable {Category}",e.GetType().Name);return StatusCode(503);}
    }
}
