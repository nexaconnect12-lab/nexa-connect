using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexaConnect.Contracts.Platform;
using NexaConnect.Infrastructure.Authorization;
using NexaConnect.Services.Notification.Application.Messages;
using NexaConnect.Services.Notification.Application.Tenant;

namespace NexaConnect.Services.Notification.Controllers;

[ApiController]
[Authorize]
[Route("api/notification/v1/notifications")]
public sealed class NotificationsController(INotificationSender sender, INotificationTenantAuthorizer authorizer) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<NotificationMessage>> Send(SendNotification command, CancellationToken cancellationToken)
    {
        if (!await Granted(command.OrganizationId, ProductPermissions.NotificationSend, cancellationToken)) return Forbid();
        try
        {
            string actor = User.FindFirstValue("sub") ?? User.FindFirstValue("azp") ?? throw new UnauthorizedAccessException();
            NotificationMessage notification = sender.Send(command, actor);
            return CreatedAtAction(nameof(Get), new { id = notification.Id }, notification);
        }
        catch (ArgumentException exception) { return BadRequest(new { error = exception.Message }); }
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<NotificationMessage>> Get(Guid id, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(Request.Headers[TenantContextHeaders.OrganizationId], out Guid organizationId)
            || !await Granted(organizationId, ProductPermissions.NotificationRead, cancellationToken)) return Forbid();
        NotificationMessage? notification = sender.Get(organizationId, id);
        return notification is null ? NotFound() : Ok(notification);
    }

    private async Task<bool> Granted(Guid organizationId, string permission, CancellationToken cancellationToken)
    {
        if (ServiceWorkloadPrincipal.IsTrusted(User)) return true;
        return string.Equals(Request.Headers[TenantContextHeaders.ApplicationCode], "nexa_connect", StringComparison.Ordinal)
            && Request.Headers.TryGetValue("Authorization", out var authorization)
            && await authorizer.CanAccessAsync(organizationId, permission, authorization.ToString(), cancellationToken);
    }
}
