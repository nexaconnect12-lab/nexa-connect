using Microsoft.AspNetCore.Mvc;
using NexaConnect.Services.Payment.Application.Intents;

namespace NexaConnect.Services.Payment.Controllers;

[ApiController]
[Route("api/payment/v1/intents")]
public sealed class PaymentIntentsController(IPaymentIntents intents) : ControllerBase
{
    [HttpPost]
    public ActionResult<PaymentIntent> Create(CreatePaymentIntent command)
    {
        try
        {
            PaymentIntent intent = intents.Create(command);
            return CreatedAtAction(nameof(Get), new { id = intent.Id }, intent);
        }
        catch (ArgumentException exception) { return BadRequest(new { error = exception.Message }); }
    }

    [HttpGet("{id:guid}")]
    public ActionResult<PaymentIntent> Get(Guid id)
    {
        PaymentIntent? intent = intents.Get(id);
        return intent is null ? NotFound() : Ok(intent);
    }
}
