using System.Text.Json;
using NexaConnect.Services.Restaurant.Application.Configuration;
using Npgsql;
using NpgsqlTypes;
using NexaConnect.Contracts.IntegrationEvents;
using NexaConnect.Infrastructure.Messaging;

namespace NexaConnect.Services.Restaurant.Infrastructure.Persistence;

public sealed class PostgresBranchProductConfigurationRepository(NpgsqlDataSource dataSource) : IBranchProductConfigurationRepository
{
    public async Task<BranchProductConfiguration?> GetAsync(Guid organizationId, Guid branchId, CancellationToken cancellationToken)
    {
        const string sql = "SELECT b.id,b.restaurant_id,r.organization_id,b.business_configuration::text,b.concurrency_version FROM branches b JOIN restaurants r ON r.id=b.restaurant_id WHERE r.organization_id=$1 AND r.status='active' AND b.id=$2 AND b.status<>'closed';";
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(organizationId);
        command.Parameters.AddWithValue(branchId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
    }

    public async Task<BranchProductConfiguration?> UpdateAsync(Guid organizationId, Guid branchId, UpdateBranchProductConfigurationCommand request, string actor, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        string json = JsonSerializer.Serialize(new ConfigurationDocument(request.DineInEnabled, request.TakeawayEnabled, request.RequireTableForDineIn, request.ServiceChargePercent));
        const string sql = "UPDATE branches b SET business_configuration=$3,updated_at_utc=now(),updated_by=$4,concurrency_version=concurrency_version+1 FROM restaurants r WHERE b.restaurant_id=r.id AND r.organization_id=$1 AND r.status='active' AND b.id=$2 AND b.status<>'closed' AND b.concurrency_version=$5 RETURNING b.id,b.restaurant_id,r.organization_id,b.business_configuration::text,b.concurrency_version;";
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue(organizationId);
        command.Parameters.AddWithValue(branchId);
        command.Parameters.AddWithValue(NpgsqlDbType.Jsonb, json);
        command.Parameters.AddWithValue(actor);
        command.Parameters.AddWithValue(request.ExpectedVersion);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        BranchProductConfiguration? result = await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
        if (result is null) { await transaction.RollbackAsync(cancellationToken); return null; }
        await reader.DisposeAsync();
        Guid auditId=Guid.NewGuid(),correlationId=Guid.NewGuid();DateTimeOffset occurred=DateTimeOffset.UtcNow;
        await using var audit = new NpgsqlCommand("INSERT INTO branch_management_audit(id,organization_id,branch_id,action,actor_subject_id,occurred_at_utc) VALUES($1,$2,$3,'branch.configuration.updated',$4,$5);", connection, transaction);
        audit.Parameters.AddWithValue(auditId); audit.Parameters.AddWithValue(organizationId); audit.Parameters.AddWithValue(branchId); audit.Parameters.AddWithValue(actor);audit.Parameters.AddWithValue(occurred);
        await audit.ExecuteNonQueryAsync(cancellationToken);
        await TransactionalAuditOutbox.EnqueueAuditAsync(connection,transaction,new(auditId,correlationId,occurred,actor,organizationId,"branch.configuration.updated","branch-configuration",branchId.ToString("D"),"succeeded"),"restaurant.audit.v1",cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    private static BranchProductConfiguration Read(NpgsqlDataReader reader)
    {
        ConfigurationDocument value = JsonSerializer.Deserialize<ConfigurationDocument>(reader.GetString(3)) ?? new(true, true, false, 0);
        return new(reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2), value.DineInEnabled, value.TakeawayEnabled, value.RequireTableForDineIn, value.ServiceChargePercent, reader.GetInt64(4));
    }

    private sealed record ConfigurationDocument(bool DineInEnabled = true, bool TakeawayEnabled = true, bool RequireTableForDineIn = false, decimal ServiceChargePercent = 0);
}
