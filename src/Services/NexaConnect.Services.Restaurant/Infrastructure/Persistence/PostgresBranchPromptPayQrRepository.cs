using System.Text.Json;
using NexaConnect.Contracts.Media;
using NexaConnect.Infrastructure.Messaging;
using NexaConnect.Services.Restaurant.Application.Configuration;
using NexaConnect.Services.Restaurant.Domain;
using Npgsql;
using NpgsqlTypes;
using NexaConnect.Observability;

namespace NexaConnect.Services.Restaurant.Infrastructure.Persistence;

public sealed class PostgresBranchPromptPayQrRepository(NpgsqlDataSource source):IBranchPromptPayQrRepository
{
    public async Task<BranchPromptPayQrV1?> GetAsync(Guid org,Guid branch,CancellationToken ct)
    {
        await using var command=source.CreateCommand("SELECT r.id,b.concurrency_version,b.business_configuration->'PromptPayQr'->>'AssetId' FROM branches b JOIN restaurants r ON r.id=b.restaurant_id WHERE r.organization_id=$1 AND r.status='active' AND b.id=$2 AND b.status='active' AND b.currency='THB';");
        command.Parameters.AddWithValue(org);command.Parameters.AddWithValue(branch);
        await using var reader=await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct)?new(org,reader.GetGuid(0),branch,reader.GetInt64(1),reader.IsDBNull(2)?null:Guid.Parse(reader.GetString(2))):null;
    }
    public async Task<BranchPromptPayQrV1?> SaveAsync(Guid org,Guid branch,PromptPayQrApproval approval,string actor,CancellationToken ct)
    {
        await using var connection=await source.OpenConnectionAsync(ct);await using var transaction=await connection.BeginTransactionAsync(ct);
        await using var command=new NpgsqlCommand("UPDATE branches b SET business_configuration=jsonb_set(b.business_configuration,'{PromptPayQr}',$3,true),concurrency_version=b.concurrency_version+1,updated_by=$4,updated_at_utc=now() FROM restaurants r WHERE b.restaurant_id=r.id AND r.organization_id=$1 AND r.status='active' AND b.id=$2 AND b.status='active' AND b.currency='THB' AND b.concurrency_version=$5 RETURNING r.id,b.concurrency_version;",connection,transaction);
        command.Parameters.AddWithValue(org);command.Parameters.AddWithValue(branch);command.Parameters.AddWithValue(NpgsqlDbType.Jsonb,JsonSerializer.Serialize(new{approval.AssetId}));
        command.Parameters.AddWithValue(actor);command.Parameters.AddWithValue(approval.ExpectedVersion);
        await using var reader=await command.ExecuteReaderAsync(ct);
        if(!await reader.ReadAsync(ct)){await reader.DisposeAsync();await transaction.RollbackAsync(ct);return null;}
        var result=new BranchPromptPayQrV1(org,reader.GetGuid(0),branch,reader.GetInt64(1),approval.AssetId);await reader.DisposeAsync();
        var id=Guid.NewGuid();var now=DateTimeOffset.UtcNow;
        await using var audit=new NpgsqlCommand("INSERT INTO branch_management_audit(id,organization_id,branch_id,action,actor_subject_id,occurred_at_utc) VALUES($1,$2,$3,'branch.configuration.updated',$4,$5);",connection,transaction);
        audit.Parameters.AddWithValue(id);audit.Parameters.AddWithValue(org);audit.Parameters.AddWithValue(branch);audit.Parameters.AddWithValue(actor);audit.Parameters.AddWithValue(now);await audit.ExecuteNonQueryAsync(ct);
        var correlation=Guid.TryParse(CorrelationContext.Current,out var current)?current:Guid.NewGuid();
        await TransactionalAuditOutbox.EnqueueAuditAsync(connection,transaction,new(id,correlation,now,actor,org,"branch.configuration.updated","branch-promptpay-qr",$"{branch:N}/{approval.AssetId?.ToString("N")??"removed"}/{result.Version}","succeeded"),"restaurant.audit.v1",ct);
        await transaction.CommitAsync(ct);return result;
    }
}
