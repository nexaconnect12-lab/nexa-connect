namespace NexaConnect.PaymentProviderSimulator;

// Test-only state survives Payment restarts, never simulator restarts.
internal static class RefundSimulator
{
    internal sealed record Command(Guid RefundId, Guid PaymentIntentId, string ProviderCaptureId, decimal Amount, string Currency);
    internal sealed record Fault(string Mode);
    public static void Map(WebApplication app, Func<HttpRequest, bool> authorized, object gate,
        Func<Command, bool> captured)
    {
        var originals = new Dictionary<Guid, Command>();
        var modes = new Dictionary<Guid, string>();
        var calls = new Dictionary<Guid, int>();
        var reads = new Dictionary<Guid, int>();
        app.MapPost("/acceptance/refunds/{intent:guid}", (Guid intent, Fault fault, HttpRequest request) =>
        {
            if (!authorized(request)) return Results.Unauthorized();
            if (fault.Mode is not ("normal" or "drop_response" or "fail" or "unknown_status")) return Results.BadRequest();
            lock (gate) modes[intent] = fault.Mode;
            return Results.Ok();
        });
        app.MapPost("/v1/refunds", (Command command, HttpContext context) =>
        {
            if (!authorized(context.Request)) return Results.Unauthorized();
            if (command.RefundId == Guid.Empty || command.Amount <= 0 || command.Currency != "THB"
                || context.Request.Headers["Idempotency-Key"] != $"refund:{command.RefundId:D}") return Results.BadRequest();
            string mode;
            lock (gate)
            {
                calls[command.RefundId] = calls.GetValueOrDefault(command.RefundId) + 1;
                if (!captured(command)) return Results.BadRequest();
                if (originals.TryGetValue(command.RefundId, out var original))
                {
                    if (original != command) return Results.Conflict();
                }
                else
                {
                    mode = modes.GetValueOrDefault(command.PaymentIntentId, "normal");
                    if (mode == "fail") return Results.Json(new { succeeded = false });
                    decimal reserved = originals.Values.Where(x => x.PaymentIntentId == command.PaymentIntentId).Sum(x => x.Amount);
                    if (!captured(command with { Amount = reserved + command.Amount })) return Results.Conflict();
                    originals[command.RefundId] = command;
                }
                mode = modes.GetValueOrDefault(command.PaymentIntentId, "normal");
            }
            if (mode is "drop_response" or "unknown_status") { context.Abort(); return Results.Empty; }
            return Results.Json(new { succeeded = true, providerTransactionId = $"sim-refund-{command.RefundId:N}" });
        });
        app.MapGet("/v1/refunds/{id:guid}", (Guid id, HttpRequest request) =>
        {
            if (!authorized(request)) return Results.Unauthorized();
            lock (gate)
            {
                reads[id] = reads.GetValueOrDefault(id) + 1;
                if (!originals.TryGetValue(id, out var original)) return Results.NotFound();
                return modes.GetValueOrDefault(original.PaymentIntentId) == "unknown_status"
                    ? Results.Json(new { status = "unknown" })
                    : Results.Json(new { status = "refunded", providerTransactionId = $"sim-refund-{id:N}" });
            }
        });
        app.MapGet("/acceptance/refunds/{id:guid}", (Guid id, HttpRequest request) =>
        {
            if (!authorized(request)) return Results.Unauthorized();
            lock (gate) return Results.Json(new { commandRequests = calls.GetValueOrDefault(id),
                statusRequests = reads.GetValueOrDefault(id), completed = originals.ContainsKey(id) });
        });
    }
}
