using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using NexaConnect.Infrastructure.Authentication;
using Npgsql;

namespace NexaConnect.Services.POS.Controllers;

[ApiController]
[Route("api/pos/v1/shifts")]
public sealed class ShiftsController(
    NpgsqlDataSource dataSource,
    RestaurantHierarchyClient hierarchy,
    IHttpClientFactory clients,
    IConfiguration configuration) : ControllerBase
{
    [HttpPost("{shiftId:guid}/close")]
    public async Task<IActionResult> CloseAsync(Guid shiftId, CancellationToken cancellationToken)
    {
        string? subject = User.FindFirst(NexaAuthenticationDefaults.SubjectClaim)?.Value;
        string token = Request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(token)) return Forbid();
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        const string scopeSql = """
            SELECT store.branch_id, store.restaurant_id
            FROM shifts shift JOIN stores store ON store.id = shift.store_id
            WHERE shift.id = $1 AND shift.status = 'open';
            """;
        await using var scopeCommand = new NpgsqlCommand(scopeSql, connection);
        scopeCommand.Parameters.AddWithValue(shiftId);
        await using NpgsqlDataReader reader = await scopeCommand.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return NotFound();
        Guid branchId = reader.GetGuid(0);
        Guid restaurantId = reader.GetGuid(1);
        await reader.CloseAsync();
        RestaurantAuthorizationScope scope = await hierarchy.GetScopeAsync(branchId, cancellationToken);
        if (scope.RestaurantId != restaurantId) return Forbid();
        var authorization = clients.CreateClient();
        authorization.BaseAddress = new Uri(configuration["Services:Authorization"]!);
        authorization.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using HttpResponseMessage response = await authorization.PostAsJsonAsync("api/authorization/v1/decisions",
            new { scope.OrganizationId, scope.RestaurantId, scope.BranchId, Permission = "pos.shift.close", Amount = (decimal?)null, Currency = (string?)null }, cancellationToken);
        response.EnsureSuccessStatusCode();
        AuthorizationDecisionResponse decision = (await response.Content.ReadFromJsonAsync<AuthorizationDecisionResponse>(cancellationToken))!;
        if (!decision.Granted) return Forbid();
        const string closeSql = "UPDATE shifts SET status = 'closed', closed_at_utc = now(), closed_by = $2, updated_at_utc = now(), concurrency_version = concurrency_version + 1 WHERE id = $1 AND status = 'open';";
        await using var close = new NpgsqlCommand(closeSql, connection);
        close.Parameters.AddWithValue(shiftId);
        close.Parameters.AddWithValue(subject);
        return await close.ExecuteNonQueryAsync(cancellationToken) == 1 ? NoContent() : Conflict();
    }

    [HttpPost("open")]
    public async Task<ActionResult<OpenShiftResponse>> OpenAsync(
        OpenShiftRequest request, CancellationToken cancellationToken)
    {
        string? subject = User.FindFirst(NexaAuthenticationDefaults.SubjectClaim)?.Value;
        string token = Request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(token)) return Forbid();
        RestaurantAuthorizationScope scope = await hierarchy.GetScopeAsync(request.BranchId, cancellationToken);
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        const string terminalSql = """
            SELECT EXISTS
            (
                SELECT 1 FROM stores store
                JOIN terminals terminal ON terminal.store_id = store.id
                WHERE store.id = $1 AND store.restaurant_id = $2 AND store.branch_id = $3
                  AND store.operational_status = 'active' AND terminal.id = $4
                  AND terminal.registration_status = 'active'
            );
            """;
        await using (var terminal = new NpgsqlCommand(terminalSql, connection))
        {
            terminal.Parameters.AddWithValue(request.StoreId);
            terminal.Parameters.AddWithValue(scope.RestaurantId);
            terminal.Parameters.AddWithValue(scope.BranchId);
            terminal.Parameters.AddWithValue(request.TerminalId);
            if (!(bool)(await terminal.ExecuteScalarAsync(cancellationToken) ?? false)) return Forbid();
        }
        var authorization = clients.CreateClient();
        authorization.BaseAddress = new Uri(configuration["Services:Authorization"]!);
        authorization.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using HttpResponseMessage decisionResponse = await authorization.PostAsJsonAsync("api/authorization/v1/decisions",
            new { scope.OrganizationId, scope.RestaurantId, scope.BranchId, Permission = "pos.shift.open", Amount = (decimal?)null, Currency = (string?)null }, cancellationToken);
        decisionResponse.EnsureSuccessStatusCode();
        AuthorizationDecisionResponse decision = (await decisionResponse.Content.ReadFromJsonAsync<AuthorizationDecisionResponse>(cancellationToken))!;
        if (!decision.Granted) return Forbid();
        Guid shiftId = Guid.NewGuid();
        const string sql = """INSERT INTO shifts (id, store_id, terminal_id, employee_identity_subject_id, shift_number, status, opened_at_utc, opened_by, created_at_utc, updated_at_utc, authorization_decision_id) VALUES ($1,$2,$3,$4,$5,'open',now(),$4,now(),now(),$6);""";
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(shiftId); command.Parameters.AddWithValue(request.StoreId); command.Parameters.AddWithValue(request.TerminalId); command.Parameters.AddWithValue(subject); command.Parameters.AddWithValue(request.ShiftNumber); command.Parameters.AddWithValue(decision.DecisionId);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return Ok(new OpenShiftResponse(shiftId, decision.DecisionId));
    }
}

public sealed record OpenShiftRequest(Guid BranchId, Guid StoreId, Guid TerminalId, string ShiftNumber);
public sealed record OpenShiftResponse(Guid ShiftId, Guid AuthorizationDecisionId);
public sealed record AuthorizationDecisionResponse(Guid DecisionId, bool Granted, decimal? EvaluatedLimit);
