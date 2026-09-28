using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexaConnect.Contracts.Platform;
using NexaConnect.Infrastructure.Authentication;
using NexaConnect.Services.Restaurant.Application.Configuration;

namespace NexaConnect.Services.Restaurant.Controllers;

[ApiController, Authorize(Policy = NexaAuthorizationPolicies.ServiceWorkload)]
[Route("api/restaurant/v1/branches/{branchId:guid}/pricing")]
public sealed class BranchPricingController(BranchProductConfigurationService service, ILogger<BranchPricingController> logger) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(Guid branchId, CancellationToken cancellationToken)
    {
        if (User.FindFirstValue("azp") != "nexaconnect-order-service"
            || !Guid.TryParse(Request.Headers[TenantContextHeaders.OrganizationId], out Guid organizationId)
            || organizationId == Guid.Empty
            || Request.Headers[TenantContextHeaders.ApplicationCode] != "nexa_connect")
        {
            logger.LogWarning("Branch pricing workload authorization denied");
            return Forbid();
        }
        var value = await service.GetPricingAsync(organizationId, branchId, cancellationToken);
        return value is null ? NotFound() : Ok(value);
    }
}
