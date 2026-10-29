using NexaConnect.Services.POS.Domain.Drawers;
using NpgsqlTypes;
namespace NexaConnect.Services.POS.Infrastructure.Persistence;

public sealed partial class PostgresDrawerIntentRepository
{
    public async Task<DrawerIntent?> FindHistoryAsync(DrawerHistoryScope s,string actor,Guid operation,CancellationToken ct)
    {
        await using var command=source.CreateCommand(Projection+" WHERE d.organization_id=$1 AND d.restaurant_id=$2 AND d.branch_id=$3 AND d.store_id=$4 AND d.terminal_id=$5 AND d.actor_subject_id=$6 AND d.operation_id=$7;");
        foreach(var id in new[]{s.OrganizationId,s.RestaurantId,s.BranchId,s.StoreId,s.TerminalId})command.Parameters.AddWithValue(id);command.Parameters.AddWithValue(actor);command.Parameters.AddWithValue(operation);
        await using var reader=await command.ExecuteReaderAsync(ct);return await reader.ReadAsync(ct)?Read(reader):null;
    }
    public async Task<bool> MatchesAsync(DrawerHistoryScope scope,CancellationToken ct)
    {
        await using var command=source.CreateCommand("SELECT EXISTS(SELECT 1 FROM stores s JOIN terminals t ON t.store_id=s.id AND t.restaurant_id=s.restaurant_id WHERE s.restaurant_id=$1 AND s.branch_id=$2 AND s.id=$3 AND t.id=$4);");
        foreach(var id in new[]{scope.RestaurantId,scope.BranchId,scope.StoreId,scope.TerminalId})command.Parameters.AddWithValue(id);
        return await command.ExecuteScalarAsync(ct) is true;
    }
    public async Task<IReadOnlyList<DrawerIntent>> ReadAsync(DrawerHistoryQuery query,CancellationToken ct)
    {
        await using var command=source.CreateCommand(Projection+"""
             WHERE d.organization_id=$1 AND d.restaurant_id=$2 AND d.branch_id=$3 AND d.store_id=$4 AND d.terminal_id=$5
             AND d.actor_subject_id=$6 AND d.reserved_at_utc >= $7 AND d.reserved_at_utc < $8
             AND($9::timestamptz IS NULL OR(d.reserved_at_utc,d.operation_id)<($9,$10::uuid))
             ORDER BY d.reserved_at_utc DESC,d.operation_id DESC LIMIT $11;
            """);
        var s=query.Scope;foreach(var id in new[]{s.OrganizationId,s.RestaurantId,s.BranchId,s.StoreId,s.TerminalId})command.Parameters.AddWithValue(id);
        command.Parameters.AddWithValue(query.Actor);command.Parameters.AddWithValue(query.FromUtc);command.Parameters.AddWithValue(query.ToUtc);
        Nullable(command,query.BeforeUtc,NpgsqlDbType.TimestampTz);Nullable(command,query.BeforeOperation,NpgsqlDbType.Uuid);command.Parameters.AddWithValue(query.Limit+1);
        var result=new List<DrawerIntent>();await using var reader=await command.ExecuteReaderAsync(ct);while(await reader.ReadAsync(ct))result.Add(Read(reader));return result;
    }
}
