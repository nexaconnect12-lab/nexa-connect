using Microsoft.AspNetCore.Mvc;
using NexaConnect.Services.Kitchen.Application;

namespace NexaConnect.Services.Kitchen.Controllers;

[ApiController]
[Route("api/kitchen/v1/tickets")]
public sealed class KitchenTicketsController(IKitchenTicketStore tickets) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<KitchenTicket>> Create(
        CreateKitchenTicket command,
        CancellationToken cancellationToken)
    {
        try
        {
            KitchenTicket ticket = await tickets.CreateAsync(command, cancellationToken);
            return CreatedAtAction(nameof(Get), new { ticketId = ticket.TicketId }, ticket);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }

    [HttpGet("{ticketId:guid}")]
    public async Task<ActionResult<KitchenTicket>> Get(Guid ticketId, CancellationToken cancellationToken)
    {
        KitchenTicket? ticket = await tickets.GetAsync(ticketId, cancellationToken);
        return ticket is null ? NotFound() : Ok(ticket);
    }

    [HttpPost("{orderId:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid orderId, CancellationToken cancellationToken)
    {
        await tickets.CancelAsync(orderId, cancellationToken);
        return NoContent();
    }
}
