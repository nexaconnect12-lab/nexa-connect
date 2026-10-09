using System.Security.Cryptography;
using System.Text.Json;
using NexaConnect.Contracts.IntegrationEvents;
using NexaConnect.Infrastructure.Persistence;
using NexaConnect.Services.Order.Domain;
using Npgsql;

namespace NexaConnect.Services.Order.Infrastructure.Messaging;

public sealed class LatePaymentReconciliation(NpgsqlDataSource source)
{
    public async Task<string> DispositionAsync(IIntegrationEvent message,string eventType,CancellationToken ct)
    {
        var bytes=JsonSerializer.SerializeToUtf8Bytes(message,message.GetType());using var document=JsonDocument.Parse(bytes);
        return await new PostgresLateFinancialWork(source).DispositionAsync(document.RootElement.GetProperty("OrganizationId").GetGuid(),eventType,
            message.EventId.ToString("D"),Convert.ToHexStringLower(SHA256.HashData(bytes)),ct);
    }
    public async Task<bool> CaptureAsync(IIntegrationEvent message, string eventType, CancellationToken ct)
    {
        var original = JsonSerializer.SerializeToUtf8Bytes(message, message.GetType());
        using var json = JsonDocument.Parse(original);
        var root = json.RootElement;
        var organization = root.GetProperty("OrganizationId").GetGuid();
        var order = root.GetProperty("OrderId").GetGuid();
        var intent = root.GetProperty("PaymentIntentId").GetGuid();
        if (organization == Guid.Empty || order == Guid.Empty || intent == Guid.Empty || message.EventId == Guid.Empty) throw new ArgumentException();
        await using var q = source.CreateCommand("SELECT restaurant_id,branch_id,status,concurrency_version,created_at_utc,receipt_snapshot->>'PaidAtUtc',receipt_snapshot IS NOT NULL FROM orders WHERE organization_id=$1 AND id=$2 AND payment_intent_id=$3");
        q.Parameters.AddWithValue(organization); q.Parameters.AddWithValue(order); q.Parameters.AddWithValue(intent);
        Guid restaurant, branch; FinancialRecord before;
        await using (var rows = await q.ExecuteReaderAsync(ct))
        {
            if (!await rows.ReadAsync(ct)) return false;
            restaurant = rows.GetGuid(0); branch = rows.GetGuid(1);
            before = new(order, rows.GetString(2), rows.GetInt64(3), rows.GetFieldValue<DateTimeOffset>(4),
                rows.IsDBNull(5) ? null : DateTimeOffset.Parse(rows.GetString(5), System.Globalization.CultureInfo.InvariantCulture), rows.GetBoolean(6));
        }
        // Retain only normalized workflow identity. Arbitrary provider failure text is never retained or logged here.
        var outcome=root.TryGetProperty("Outcome",out var outcomeValue)?outcomeValue.GetString():root.TryGetProperty("Status",out var statusValue)?statusValue.GetString():null;
        outcome=outcome is "authorized" or "captured" or "failed" or "cancelled" or "voided" or "unknown" or "succeeded"?outcome:null;
        var payload = JsonSerializer.Serialize(new { message.EventId, message.CorrelationId, message.OccurredAtUtc, OrganizationId = organization,
            OrderId = order, PaymentIntentId = intent, EventType = eventType,Outcome=outcome });
        var fingerprint = Convert.ToHexStringLower(SHA256.HashData(original));
        return await new PostgresLateFinancialWork(source).CaptureAsync(organization, restaurant, branch, eventType, message.EventId.ToString("D"),
            fingerprint, payload, (from, to) => !FinancialDayFence.AllowsMutation(from, to, before, null, false, "orders"), ct);
    }
}
