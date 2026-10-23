using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexaConnect.Infrastructure.Authentication;
using NexaConnect.Services.Catalog.Application.Menu;

namespace NexaConnect.Services.Catalog.Controllers;

[ApiController, Authorize(Policy = NexaAuthorizationPolicies.ServiceWorkload)]
[Route("api/catalog/v1/internal/organizations/{organization:guid}/branches/{branch:guid}/guest-menu")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class InternalGuestMenuController(GuestMenuRead service, ILogger<InternalGuestMenuController> logger) : ControllerBase
{
    [HttpGet] public IActionResult Get(Guid organization, Guid branch)
    {
        if (User.FindFirst("azp")?.Value != "nexaconnect-order-service")
        { logger.LogWarning("QR menu workload denied"); return Forbid(); }
        try { return Ok(service.Read(organization, branch)); }
        catch (ArgumentException) { logger.LogWarning("QR menu scope or bounds rejected"); return Conflict(); }
    }
}
