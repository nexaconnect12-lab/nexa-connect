using System.Security.Cryptography;
using System.Text.Json;
using NexaConnect.Infrastructure.Persistence;
using NexaConnect.Services.Payment.Application.Intents;
using NexaConnect.Services.Payment.Application.Webhooks;
using NexaConnect.Services.Payment.Domain;
using Npgsql;

namespace NexaConnect.Services.Payment.Infrastructure.Webhooks;

public sealed class LateAwareWebhookRecovery(WebhookPaymentRecovery recovery, NpgsqlDataSource source) : IWebhookPaymentRecovery
{
    public Task<bool> ReconcileAsync(PaymentIntent intent, PaymentMutationContext context, CancellationToken ct) => recovery.ReconcileAsync(intent, context, ct);
    public async Task<OmiseWebhookProcessResult> ReconcileEventAsync(string eventId, PaymentIntent intent, PaymentMutationContext context, CancellationToken ct)
    {
        var payload = JsonSerializer.Serialize(new { EventId = eventId, intent.OrganizationId, intent.RestaurantId, intent.BranchId, intent.OrderId, IntentId = intent.Id });
        var fingerprint = Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(payload)));
        var custody=new PostgresLateFinancialWork(source);
        var disposition=await custody.DispositionAsync(intent.OrganizationId,"payment.omise-webhook-reference.v1",eventId,fingerprint,ct);
        if(disposition is "committed" or "armed")return new(disposition=="committed"?"late_captured":"barrier_held",false);
        try
        {
            var committed = await recovery.ReconcileAsync(intent, context, ct);
            return new(committed ? "completed" : "retry", committed);
        }
        catch (PostgresException e) when (e.SqlState == "PDS01")
        {
            await using var q = source.CreateCommand("SELECT status,concurrency_version,created_at_utc FROM payment_intents WHERE organization_id=$1 AND id=$2 AND restaurant_id=$3 AND branch_id=$4 AND order_id=$5");
            q.Parameters.AddWithValue(intent.OrganizationId); q.Parameters.AddWithValue(intent.Id); q.Parameters.AddWithValue(intent.RestaurantId);
            q.Parameters.AddWithValue(intent.BranchId); q.Parameters.AddWithValue(intent.OrderId);
            FinancialRecord before;
            await using (var rows = await q.ExecuteReaderAsync(ct))
            {
                if (!await rows.ReadAsync(ct)) throw;
                before = new(intent.Id, rows.GetString(0), rows.GetInt64(1), rows.GetFieldValue<DateTimeOffset>(2));
            }
            // Canonical webhook validation already bound this event to the intent. Store no provider identifiers or body.
            var captured = await custody.CaptureAsync(intent.OrganizationId, intent.RestaurantId, intent.BranchId,
                "payment.omise-webhook-reference.v1", eventId, fingerprint, payload,
                (from, to) => !FinancialDayFence.AllowsMutation(from, to, before, null, false, "payment_intents"), ct);
            return new(captured ? "late_captured" : "barrier_held", false);
        }
    }
}
