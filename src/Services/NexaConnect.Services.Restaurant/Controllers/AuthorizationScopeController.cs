using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexaConnect.Infrastructure.Authentication;
using Npgsql;

namespace NexaConnect.Services.Restaurant.Controllers;

[ApiController]
[Route("api/restaurant/v1/branches")]
public sealed class AuthorizationScopeController(NpgsqlDataSource dataSource) : ControllerBase
{
    [Authorize(Policy = NexaAuthorizationPolicies.PosWorkload)]
    [HttpGet("{branchId:guid}/authorization-scope")]
    public async Task<ActionResult<AuthorizationScopeResponse>> GetAsync(
        Guid branchId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT restaurant.organization_id, restaurant.id, branch.id
            FROM branches branch
            JOIN restaurants restaurant ON restaurant.id = branch.restaurant_id
            WHERE branch.id = $1 AND branch.status = 'active' AND restaurant.status = 'active';
            """;
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(branchId);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return NotFound();
        return Ok(new AuthorizationScopeResponse(reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2)));
    }
}

public sealed record AuthorizationScopeResponse(Guid OrganizationId, Guid RestaurantId, Guid BranchId);
