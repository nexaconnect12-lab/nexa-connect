using System.Text.Json;
using NexaConnect.Contracts.Media;
using NexaConnect.Contracts.IntegrationEvents;
using NexaConnect.Infrastructure.Messaging;
using NexaConnect.Services.Restaurant.Application.Configuration;
using Npgsql;
using NpgsqlTypes;

namespace NexaConnect.Services.Restaurant.Infrastructure.Persistence;

public sealed class PostgresBranchGalleryRepository(NpgsqlDataSource source) : IBranchGalleryRepository
{
    public async Task<Guid?> RestaurantAsync(Guid org, Guid branch, CancellationToken ct)
    {
        await using var command = source.CreateCommand("SELECT r.id FROM branches b JOIN restaurants r ON r.id=b.restaurant_id WHERE r.organization_id=$1 AND r.status='active' AND b.id=$2 AND b.status='active';");
        command.Parameters.AddWithValue(org); command.Parameters.AddWithValue(branch);
        return await command.ExecuteScalarAsync(ct) is Guid restaurant ? restaurant : null;
    }
    public async Task<BranchGallery?> GetAsync(Guid org, Guid branch, CancellationToken ct)
    {
        await using var command = source.CreateCommand("SELECT b.concurrency_version,b.business_configuration->'ScreensaverGallery' FROM branches b JOIN restaurants r ON r.id=b.restaurant_id WHERE r.organization_id=$1 AND r.status='active' AND b.id=$2 AND b.status='active';");
        command.Parameters.AddWithValue(org); command.Parameters.AddWithValue(branch);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        var update = reader.IsDBNull(1) ? new UpdateBranchGallery(1, [], []) : JsonSerializer.Deserialize<UpdateBranchGallery>(reader.GetString(1))!;
        return new(org, branch, reader.GetInt64(0), update.Staff, update.Customer, update.SecondsPerSlide);
    }
    public async Task<BranchGallery?> SaveAsync(Guid org, Guid branch, UpdateBranchGallery update, string actor, CancellationToken ct)
    {
        await using var connection = await source.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await using var command = new NpgsqlCommand("UPDATE branches b SET business_configuration=jsonb_set(b.business_configuration,'{ScreensaverGallery}',$3,true),concurrency_version=b.concurrency_version+1,updated_by=$4,updated_at_utc=now() FROM restaurants r WHERE b.restaurant_id=r.id AND r.organization_id=$1 AND r.status='active' AND b.id=$2 AND b.status='active' AND b.concurrency_version=$5 RETURNING b.concurrency_version;",connection,transaction);
        command.Parameters.AddWithValue(org); command.Parameters.AddWithValue(branch); command.Parameters.AddWithValue(NpgsqlDbType.Jsonb, JsonSerializer.Serialize(update));
        command.Parameters.AddWithValue(actor); command.Parameters.AddWithValue(update.ExpectedVersion);
        var version = await command.ExecuteScalarAsync(ct);
        if (version is not long next) { await transaction.RollbackAsync(ct); return null; }
        var id=Guid.NewGuid();var now=DateTimeOffset.UtcNow;
        await using var audit = new NpgsqlCommand("INSERT INTO branch_management_audit(id,organization_id,branch_id,action,actor_subject_id,occurred_at_utc) VALUES($1,$2,$3,'branch.configuration.updated',$4,$5);",connection,transaction);
        audit.Parameters.AddWithValue(id);audit.Parameters.AddWithValue(org);audit.Parameters.AddWithValue(branch);audit.Parameters.AddWithValue(actor);audit.Parameters.AddWithValue(now);
        await audit.ExecuteNonQueryAsync(ct);
        await TransactionalAuditOutbox.EnqueueAuditAsync(connection,transaction,new(id,Guid.NewGuid(),now,actor,org,"branch.configuration.updated","branch-configuration",branch.ToString("D"),"succeeded"),"restaurant.audit.v1",ct);
        await transaction.CommitAsync(ct);
        return new(org,branch,next,update.Staff,update.Customer,update.SecondsPerSlide);
    }
}
