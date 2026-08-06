using Microsoft.AspNetCore.Mvc;
using NexaConnect.Services.Notification.Application.Messages;

namespace NexaConnect.Services.Notification.Controllers;

[ApiController]
[Route("api/notification/v1/notifications")]
public sealed class NotificationsController(INotificationSender sender) : ControllerBase
{
    [HttpPost]
    public ActionResult<NotificationMessage> Send(SendNotification command)
    {
        try
        {
            NotificationMessage notification = sender.Send(command);
            return CreatedAtAction(nameof(Get), new { id = notification.Id }, notification);
        }
        catch (ArgumentException exception) { return BadRequest(new { error = exception.Message }); }
    }

    [HttpGet("{id:guid}")]
    public ActionResult<NotificationMessage> Get(Guid id)
    {
        NotificationMessage? notification = sender.Get(id);
        return notification is null ? NotFound() : Ok(notification);
    }
}
