using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using NexaConnect.Contracts.RestaurantQr;
using NexaConnect.CustomerBff.Application.Qr;
using NexaConnect.Infrastructure.Authentication;
using NexaConnect.Infrastructure.Http;

namespace NexaConnect.CustomerBff.Controllers;

public sealed class QrGuestOptions
{
    public bool Enabled { get; set; }
    public bool MenuEnabled { get; set; }
    public bool RequestsEnabled { get; set; }
    public string PublicOrigin { get; set; } = "";
    public static bool ValidOrigin(string value) => value.Length <= 256 && Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme == "https" && uri.AbsolutePath == "/" && uri.Query == "" && uri.Fragment == "" && uri.UserInfo == "";
    public string Origin => new Uri(PublicOrigin).GetLeftPart(UriPartial.Authority);
}

[ApiController, AllowAnonymous, Route("bff/guest/qr")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class QrGuestEntryController(IQrGatewayPort port, IOptions<QrGuestOptions> settings, BoundedRequestGate gate, ILogger<QrGuestEntryController> logger) : ControllerBase
{
    private const string Cookie = "__Host-nexa-qr-guest";
    [HttpPost("sessions"), RequestSizeLimit(1024)]
    public Task<IActionResult> Start(QrEntryExchange input, CancellationToken ct) => Execute("start", input, ct);
    [HttpGet("context")] public Task<IActionResult> Context(CancellationToken ct) => Execute("context", null, ct);
    [HttpDelete("sessions")] public Task<IActionResult> End(CancellationToken ct) => Execute("end", null, ct);
    [HttpGet("menu")] public Task<IActionResult> Menu(CancellationToken ct) => Shopping("menu", null, ct);
    [HttpPost("quote"), RequestSizeLimit(8192)] public Task<IActionResult> Quote(QrCart cart, CancellationToken ct) => Shopping("quote", cart, ct);
    [HttpGet("requests")] public Task<IActionResult> Mine(CancellationToken ct) => Shopping("mine",null,ct);
    [HttpPost("requests"),RequestSizeLimit(1024)] public Task<IActionResult> Submit(QrSubmission input,CancellationToken ct) => Shopping("submit",input,ct);
    private async Task<IActionResult> Shopping(string operation, object? cart, CancellationToken ct)
    {
        if (!settings.Value.Enabled || !settings.Value.MenuEnabled) return NotFound();
        if(operation is "mine" or "submit" && !settings.Value.RequestsEnabled)return NotFound();
        string origin = settings.Value.Origin;
        if (!Request.IsHttps || !string.Equals($"https://{Request.Host}", origin, StringComparison.OrdinalIgnoreCase)
            || operation is "quote" or "submit" && (!Request.HasJsonContentType() || !string.Equals(Request.Headers.Origin.ToString(), origin, StringComparison.OrdinalIgnoreCase)))
        { logger.LogWarning("QR shopping origin rejected"); return StatusCode(403); }
        if (!gate.Admit(HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"))
        { logger.LogWarning("QR shopping rate limited"); Response.Headers.RetryAfter = "60"; return StatusCode(429); }
        string? session = Request.Cookies[Cookie];
        if (!ValidToken(session)) return Unauthorized();
        try
        {
            var reply = operation is "mine" or "submit"?await port.RequestsAsync(session!,cart as QrSubmission,ct):await port.ShoppingAsync(operation, session!, cart as QrCart, ct);
            if (reply.Status is < 200 or >= 300)
            {
                logger.LogWarning("QR shopping rejected; operation {Operation}, status {Status}", operation, reply.Status);
                if (reply.Status == 401) Response.Cookies.Delete(Cookie, CookieOptions());
                if (reply.Status == 429) Response.Headers.RetryAfter = "60";
                return StatusCode(SafeStatus(reply.Status), new { title = "Menu or cart unavailable. Refresh and review before continuing." });
            }
            // Typed allowlist prevents internal dependency fields from reaching guests.
            if(operation=="mine")return Ok(reply.Body?.Deserialize<QrRequestList>(JsonSerializerOptions.Web)??throw new JsonException());
            if(operation=="submit")return Ok(reply.Body?.Deserialize<QrRequestView>(JsonSerializerOptions.Web)??throw new JsonException());
            if (operation == "menu")
            {
                var menu = reply.Body?.Deserialize<QrMenu>(JsonSerializerOptions.Web);
                if (menu?.Items is null || menu.Items.Any(i => i is null)) throw new JsonException();
                return Ok(menu);
            }
            var quote = reply.Body?.Deserialize<QrQuote>(JsonSerializerOptions.Web);
            if (quote?.Lines is null || quote.Lines.Any(l => l is null) || quote.ExpiresAtUtc <= DateTimeOffset.UtcNow) throw new JsonException();
            return Ok(quote);
        }
        catch (Exception e) when (e is HttpRequestException or JsonException || e is OperationCanceledException && !ct.IsCancellationRequested)
        { logger.LogWarning("QR shopping dependency unavailable; operation {Operation}", operation); return StatusCode(503); }
    }
    private async Task<IActionResult> Execute(string operation, QrEntryExchange? input, CancellationToken ct)
    {
        if (!settings.Value.Enabled) return NotFound();
        string origin = settings.Value.Origin;
        if (!Request.IsHttps || !string.Equals($"https://{Request.Host}", origin, StringComparison.OrdinalIgnoreCase)) return StatusCode(403);
        if (operation != "context" && (!string.Equals(Request.Headers.Origin.ToString(), origin, StringComparison.OrdinalIgnoreCase)
            || operation == "start" && !Request.HasJsonContentType()))
        { logger.LogWarning("QR guest origin rejected"); return StatusCode(403); }
        if (!gate.Admit(HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"))
        { logger.LogWarning("QR guest transport admission limited"); Response.Headers.RetryAfter = "60"; return StatusCode(429); }
        if (input is not null && !ValidToken(input.EntryToken)) return BadRequest();
        string? session = Request.Cookies[Cookie];
        if (operation != "start" && !ValidToken(session)) return Unauthorized();
        try
        {
            var reply = await port.GuestAsync(operation, operation == "start" ? null : session, input, ct);
            if (reply.Status is < 200 or >= 300)
            {
                logger.LogWarning("QR guest downstream rejected; operation {Operation}, status {Status}", operation, reply.Status);
                if (reply.Status == 401) Response.Cookies.Delete(Cookie, CookieOptions());
                if (reply.Status == 429) Response.Headers.RetryAfter = "60";
                return StatusCode(SafeStatus(reply.Status), new { title = "Table entry unavailable. Scan the current QR or ask staff." });
            }
            if (operation == "end") { Response.Cookies.Delete(Cookie, CookieOptions()); return NoContent(); }
            QrGuestContext? context;
            string? issuedToken = null;
            if (operation == "start")
            {
                var issue = reply.Body?.Deserialize<QrSessionIssue>(JsonSerializerOptions.Web);
                if (issue is null || !ValidToken(issue.SessionToken)) throw new JsonException();
                context = issue.Context;
                issuedToken = issue.SessionToken;
            }
            else context = reply.Body?.Deserialize<QrGuestContext>(JsonSerializerOptions.Web);
            if (context is null || context.VisitorId == Guid.Empty || context.EntryId == Guid.Empty
                || context.OrganizationId == Guid.Empty || context.RestaurantId == Guid.Empty || context.BranchId == Guid.Empty
                || context.ExpiresAtUtc <= DateTimeOffset.UtcNow || context.ExpiresAtUtc > DateTimeOffset.UtcNow.AddHours(2).AddSeconds(5)) throw new JsonException();
            if (issuedToken is not null) Response.Cookies.Append(Cookie, issuedToken, CookieOptions(context.ExpiresAtUtc));
            return Ok(new { context.VisitorId, context.EntryId, context.RestaurantName, context.BranchName, context.TableCode, context.Currency, context.ExpiresAtUtc, menuAvailable = settings.Value.MenuEnabled, requestsAvailable = settings.Value.RequestsEnabled && settings.Value.MenuEnabled, orderingAvailable = false });
        }
        catch (Exception e) when (e is HttpRequestException or JsonException || e is OperationCanceledException && !ct.IsCancellationRequested)
        { logger.LogWarning("QR guest dependency unavailable; operation {Operation}", operation); return StatusCode(503); }
    }
    private static bool ValidToken(string? value) => value?.Length == 43 && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
    private static CookieOptions CookieOptions(DateTimeOffset? expiry = null) => new() { HttpOnly = true, Secure = true, SameSite = SameSiteMode.Strict, Path = "/", Expires = expiry, IsEssential = true };
    internal static int SafeStatus(int status) => status is 400 or 401 or 403 or 404 or 409 or 429 ? status : 503;
}

[ApiController, Authorize(Policy = "CustomerSession"), Route("bff/customer/qr-entries")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class QrAdminController(TenantSelectionCookie cookie, BffAccessTokenService tokens, IConfiguration configuration,
    IOptions<QrGuestOptions> settings, QrGatewayService service, ILogger<QrAdminController> logger) : ControllerBase
{
    [HttpGet("csrf")] public IActionResult Csrf([FromServices] IAntiforgery antiforgery) => !settings.Value.Enabled ? NotFound() : Ok(new { requestToken = antiforgery.GetAndStoreTokens(HttpContext).RequestToken });
    [HttpGet("branches/{branch:guid}")] public Task<IActionResult> List(Guid branch, CancellationToken ct) => Send(branch, QrAdminOperation.List, null, null, ct);
    [HttpPost("branches/{branch:guid}"), ValidateAntiForgeryToken, RequestSizeLimit(1024)] public Task<IActionResult> Create(Guid branch, CreateQrEntry input, CancellationToken ct) => Send(branch, QrAdminOperation.Create, null, input, ct);
    [HttpPost("branches/{branch:guid}/{id:guid}/rotate"), ValidateAntiForgeryToken, RequestSizeLimit(1024)] public Task<IActionResult> Rotate(Guid branch, Guid id, ChangeQrEntry input, CancellationToken ct) => Send(branch, QrAdminOperation.Rotate, id, input, ct);
    [HttpPost("branches/{branch:guid}/{id:guid}/revoke"), ValidateAntiForgeryToken, RequestSizeLimit(1024)] public Task<IActionResult> Revoke(Guid branch, Guid id, ChangeQrEntry input, CancellationToken ct) => Send(branch, QrAdminOperation.Revoke, id, input, ct);
    private async Task<IActionResult> Send(Guid branch, QrAdminOperation operation, Guid? entry, object? body, CancellationToken ct)
    {
        if (!settings.Value.Enabled) return NotFound();
        var tenant = cookie.Unprotect(Request.Cookies["__Host-nexa-customer-tenant"]);
        if (tenant is null || tenant.SubjectId != User.FindFirstValue("sub")) return Unauthorized();
        var bff = configuration.GetRequiredSection("Bff");
        string? bearer = await tokens.GetValidAccessTokenAsync(HttpContext, "CustomerCookie", bff["Authority"]!, bff["ClientId"]!, bff["ClientSecret"]!, ct);
        if (string.IsNullOrWhiteSpace(bearer)) return Unauthorized();
        try
        {
            var reply = await service.StaffAsync(tenant, bearer, branch, operation, entry, body, ct);
            if (reply.Status is < 200 or >= 300)
            { logger.LogWarning("QR administration downstream rejected; operation {Operation}, status {Status}", operation, reply.Status); return StatusCode(QrGuestEntryController.SafeStatus(reply.Status), new { title = "Table QR unavailable or changed. Reload before retrying." }); }
            if (operation is QrAdminOperation.Create or QrAdminOperation.Rotate)
            {
                var issue = reply.Body?.Deserialize<QrEntryIssue>(JsonSerializerOptions.Web) ?? throw new JsonException();
                return Ok(new { issue.Entry, entryUrl = settings.Value.Origin + "/order#" + issue.EntryToken });
            }
            return Ok(reply.Body);
        }
        catch (Exception e) when (e is HttpRequestException or JsonException || e is OperationCanceledException && !ct.IsCancellationRequested)
        { logger.LogWarning("QR administration dependency unavailable"); return StatusCode(503); }
    }
}
