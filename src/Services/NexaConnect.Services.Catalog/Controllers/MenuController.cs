using Microsoft.AspNetCore.Mvc;
using NexaConnect.Services.Catalog.Application.Menu;

namespace NexaConnect.Services.Catalog.Controllers;

[ApiController]
[Route("api/catalog/v1/branches/{branchId:guid}/menu-items")]
public sealed class MenuController(IMenuCatalog catalog) : ControllerBase
{
    [HttpGet]
    public ActionResult<IReadOnlyCollection<MenuItem>> Get(Guid branchId) => Ok(catalog.GetForBranch(branchId));

    [HttpPost]
    public ActionResult<MenuItem> Add(Guid branchId, CreateMenuItem command)
    {
        try
        {
            MenuItem item = catalog.Add(branchId, command);
            return CreatedAtAction(nameof(Get), new { branchId }, item);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }
}
