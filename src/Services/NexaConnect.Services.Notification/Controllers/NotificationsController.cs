using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexaConnect.Contracts.Platform;
using NexaConnect.Infrastructure.Authorization;
using NexaConnect.Services.Notification.Application.Messages;
using NexaConnect.Services.Notification.Application.Tenant;
using NexaConnect.Services.Notification.Domain;

namespace NexaConnect.Services.Notification.Controllers;

[ApiController]
[Authorize]
[Route("api/notification/v1/notifications")]
public sealed class NotificationsController(INotificationSender sender, INotificationTenantAuthorizer authorizer,
    ILogger<NotificationsController> logger) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<NotificationMessage>> Send(SendNotificationRequest request, CancellationToken cancellationToken)
    {
        if (!await Granted(request.OrganizationId, ProductPermissions.NotificationSend, cancellationToken)) return Forbid();
        try
        {
            string actor = User.FindFirstValue("sub") ?? throw new UnauthorizedAccessException();
            NotificationMessage notification = sender.Send(new SendNotification(request.OrganizationId, request.Channel,
                request.Recipient, request.Subject, request.Body), Context(actor));
            return CreatedAtAction(nameof(Get), new { id = notification.Id }, notification);
        }
        catch (ArgumentException exception) { return BadRequest(new { error = exception.Message }); }
        catch (NotificationIdempotencyConflictException exception) { return Conflict(new { error = exception.Message }); }
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
        bool granted = string.Equals(Request.Headers[TenantContextHeaders.ApplicationCode], "nexa_connect", StringComparison.Ordinal)
            && !ServiceWorkloadPrincipal.IsTrusted(User)
            && Guid.TryParse(Request.Headers[TenantContextHeaders.OrganizationId], out Guid contextOrganizationId)
            && contextOrganizationId == organizationId
            && Request.Headers.TryGetValue("Authorization", out var authorization)
            && await authorizer.CanAccessAsync(organizationId, permission, authorization.ToString(), cancellationToken);
        if (!granted)
            logger.LogWarning("Notification authorization denied for organization {OrganizationId} and permission {Permission}.",
                organizationId, permission);
        return granted;
    }

    private NotificationMutationContext Context(string actor)
    {
        string correlation = HttpContext.TraceIdentifier;
        Guid eventCorrelation = Guid.TryParse(correlation, out Guid id) ? id
            : new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(correlation))[..16]);
        return new NotificationMutationContext(actor, eventCorrelation, correlation);
    }
}

public sealed record SendNotificationRequest(Guid OrganizationId, string Channel, string Recipient, string Subject, string Body);
