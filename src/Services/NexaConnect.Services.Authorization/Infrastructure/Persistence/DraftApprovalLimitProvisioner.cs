using Npgsql;
namespace NexaConnect.Services.Authorization.Infrastructure.Persistence;

// Reference-only tooling; does not expose an approval API or restore revoked limits.
public sealed class DraftApprovalLimitProvisioner(NpgsqlDataSource source)
{
    public async Task EnsureAsync(Guid restaurant, string subject, CancellationToken ct)
    {
        await using var connection = await source.OpenConnectionAsync(ct);
        if (!System.Text.RegularExpressions.Regex.IsMatch(connection.Database, "^nexa_review_it_[a-f0-9]{32}_authorization$"))
            throw new InvalidOperationException("Draft database required.");
        await using var command = new NpgsqlCommand("""
            INSERT INTO financial_approval_limits(id,restaurant_id,principal_type,principal_id,action_code,currency,maximum_amount,status)
            VALUES($1,$2,'subject',$3,'pos.day-close.late-cash.post','THB',1000,'active') ON CONFLICT DO NOTHING;
            """, connection);
        command.Parameters.AddWithValue(Guid.NewGuid()); command.Parameters.AddWithValue(restaurant); command.Parameters.AddWithValue(subject);
        await command.ExecuteNonQueryAsync(ct);
    }
}
