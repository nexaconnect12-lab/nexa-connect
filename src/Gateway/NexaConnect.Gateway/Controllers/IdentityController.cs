using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

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

    [Authorize(Roles = "report-viewer")]
    [HttpGet("report-access")]
    public IActionResult ReportAccess()
    {
        return Ok(new { Granted = true });
    }
}
