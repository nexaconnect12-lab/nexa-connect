using System.Text.Json;
using Microsoft.Extensions.Options;

namespace NexaConnect.Services.Payment.Infrastructure.Webhooks;

public sealed class OmiseWebhookAcceptanceOptions
{
    public bool Enabled { get; set; }
    public string? MarkerPath { get; set; }
    public string? ControlPath { get; set; }
}

public interface IOmiseWebhookProcessingBoundary
{
    Task AfterProcessingAsync(string outcome, bool financialTransitionCommitted, CancellationToken token);
}

public sealed class NoOpOmiseWebhookProcessingBoundary : IOmiseWebhookProcessingBoundary
{
    public Task AfterProcessingAsync(string outcome, bool financialTransitionCommitted, CancellationToken token) => Task.CompletedTask;
}

/// <summary>Testing-only pause after reconciliation commits and before inbox acknowledgement.</summary>
public sealed class FileOmiseWebhookProcessingBoundary(IOptions<OmiseWebhookAcceptanceOptions> options)
    : IOmiseWebhookProcessingBoundary
{
    public async Task AfterProcessingAsync(string outcome, bool financialTransitionCommitted, CancellationToken token)
    {
        OmiseWebhookAcceptanceOptions value = options.Value;
        // Busy/temporary/rejected deliveries have not established the acceptance boundary.
        // Pause only after status reconciliation reports a completed local outcome.
        if (!value.Enabled || outcome != "completed" || !financialTransitionCommitted) return;
        string marker = Path.GetFullPath(value.MarkerPath!);
        string control = Path.GetFullPath(value.ControlPath!);
        string temporary = marker + ".tmp." + Guid.NewGuid().ToString("N");
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            phase = "financial_committed_before_inbox_ack",
            outcome = outcome is "completed" or "rejected" ? outcome : "retry",
            processId = Environment.ProcessId
        });
        try
        {
            await File.WriteAllBytesAsync(temporary, payload, token);
            File.Move(temporary, marker, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
        while (!token.IsCancellationRequested)
        {
            if (File.Exists(control))
            {
                try
                {
                    using JsonDocument json = JsonDocument.Parse(await File.ReadAllBytesAsync(control, token));
                    if (json.RootElement.TryGetProperty("phase", out JsonElement phase)
                        && phase.ValueKind == JsonValueKind.String && phase.GetString() == "continue") return;
                }
                catch (JsonException) { }
                catch (IOException) { }
            }
            await Task.Delay(200, token);
        }
    }
}
