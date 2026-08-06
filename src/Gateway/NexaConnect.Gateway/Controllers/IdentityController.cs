using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexaConnect.Infrastructure.Authentication;

namespace NexaConnect.Gateway.Controllers;

[ApiController]
[Route("api/identity")]
public sealed class IdentityController : ControllerBase
{
    [Authorize]
    [HttpGet("me")]
    public IActionResult Me()
    {
        return Ok(new
        {
            Subject = User.FindFirst("sub")?.Value,
            Username = User.Identity?.Name,
            Roles = User.FindAll("roles").Select(claim => claim.Value).ToArray()
        });
    }

    [Authorize(Policy = NexaAuthorizationPolicies.ReportViewer)]
    [HttpGet("report-access")]
    public IActionResult ReportAccess()
    {
        return Ok(new { Granted = true });
    }
}
