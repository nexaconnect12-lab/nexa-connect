using Microsoft.AspNetCore.Mvc;
using NexaConnect.Services.Customer.Application.Customers;

namespace NexaConnect.Services.Customer.Controllers;

[ApiController]
[Route("api/customer/v1/organizations/{organizationId:guid}/customers")]
public sealed class CustomersController(ICustomers customers) : ControllerBase
{
    [HttpPost]
    public ActionResult<CustomerProfile> Create(Guid organizationId, CreateCustomerRequest request)
    {
        try
        {
            CustomerProfile customer = customers.Create(new CreateCustomer(organizationId, request.CustomerNumber, request.DisplayName, request.IdentitySubjectId));
            return CreatedAtAction(nameof(Get), new { organizationId, id = customer.Id }, customer);
        }
        catch (ArgumentException exception) { return BadRequest(new { error = exception.Message }); }
    }

    [HttpGet("{id:guid}")]
    public ActionResult<CustomerProfile> Get(Guid organizationId, Guid id)
    {
        CustomerProfile? customer = customers.Get(organizationId, id);
        return customer is null ? NotFound() : Ok(customer);
    }
}

public sealed record CreateCustomerRequest(string CustomerNumber, string DisplayName, string? IdentitySubjectId);
