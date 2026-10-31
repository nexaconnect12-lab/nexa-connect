using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexaConnect.Contracts.Catalog;
using NexaConnect.Contracts.Platform;
using NexaConnect.Services.Catalog.Application.Details;
using NexaConnect.Services.Catalog.Application.Menu;
using NexaConnect.Services.Catalog.Domain.Details;

namespace NexaConnect.Services.Catalog.Controllers;

[ApiController, Authorize, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("api/catalog/v1/customer/branches/{branch:guid}/menu-details")]
public sealed class MenuDetailsController(MenuDetailsService service, ILogger<MenuDetailsController> logger) : ControllerBase
{
    [HttpGet] public Task<IActionResult> Read(Guid branch, CancellationToken ct) => Execute(branch, null, null, ct);
    [HttpPut("products/{product:guid}"), RequestSizeLimit(4096)]
    public Task<IActionResult> Save(Guid branch, Guid product, UpdateMenuDetails input, CancellationToken ct) => Execute(branch, product, input, ct);
    private async Task<IActionResult> Execute(Guid branch, Guid? product, UpdateMenuDetails? input, CancellationToken ct)
    {
        if (!Guid.TryParse(Request.Headers[TenantContextHeaders.OrganizationId], out var org) || org == Guid.Empty
            || Request.Headers[TenantContextHeaders.ApplicationCode] != "nexa_connect") return BadRequest();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            string bearer = Request.Headers.Authorization.ToString();
            if (product is null) return Ok(await service.ReadAsync(org, branch, bearer, deadline.Token));
            var result = await service.ChangeAsync(org, branch, product.Value, input!, bearer,
                new(User.FindFirst("sub")?.Value ?? "", Guid.TryParse(HttpContext.TraceIdentifier, out var id) ? id : Guid.NewGuid()), deadline.Token);
            return result is null ? NotFound() : Ok(result);
        }
        catch (UnauthorizedAccessException) { logger.LogWarning("Menu details authorization denied"); return Forbid(); }
        catch (MenuDetailsConflictException) { return Conflict(new { code = "menu_details_changed" }); }
        catch (ArgumentException) { return BadRequest(new { code = "invalid_menu_details" }); }
        catch (Exception e) when (e is HttpRequestException or Npgsql.NpgsqlException || e is OperationCanceledException && !ct.IsCancellationRequested)
        { logger.LogWarning("Menu details unavailable {Category}", e.GetType().Name); return StatusCode(503); }
    }
}
