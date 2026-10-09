using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NexaConnect.Contracts.IntegrationEvents;

namespace NexaConnect.Services.POS.Application.DayClose;

public sealed record CashCorrectionReplayRequest(Guid OrganizationId, Guid BranchId, Guid RestaurantId,
    DateTimeOffset FromUtc, DateTimeOffset ToUtc, int Limit)
{
    public void Validate()
    {
        if (OrganizationId == Guid.Empty || BranchId == Guid.Empty || RestaurantId == Guid.Empty ||
            FromUtc == default || ToUtc > DateTimeOffset.UtcNow || FromUtc >= ToUtc || ToUtc - FromUtc > TimeSpan.FromDays(31) || Limit is < 1 or > 1000)
            throw new ArgumentException("Invalid replay scope, capture range or limit.");
    }
}
public sealed record CashCorrectionReplayEvent(Guid Id, string Payload)
{
    public override string ToString() => "CashCorrectionReplayEvent [redacted]";
}
public sealed record CashCorrectionReplayPlan(string Manifest, int Count);
public sealed class CashCorrectionReplayInterruptedException(Guid runId) : Exception("Replay interrupted; inspect its durable audit.")
{
    public Guid RunId { get; } = runId;
}
public interface ICashCorrectionReplayStore
{
    Task<IReadOnlyList<CashCorrectionReplayEvent>> SelectAsync(CashCorrectionReplayRequest request, CancellationToken ct);
    Task StartAsync(Guid runId, CashCorrectionReplayRequest request, Guid operatorId, string reason, string manifest, int count, CancellationToken ct);
    Task AppendAsync(Guid runId, Guid eventId, string outcome, CancellationToken ct);
}
public interface ICashCorrectionReplayTransport
{
    Task PublishAsync(CashCorrectionReplayEvent value, CancellationToken ct);
}

public sealed class CashCorrectionReplay(ICashCorrectionReplayStore store, ICashCorrectionReplayTransport transport)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public async Task<CashCorrectionReplayPlan> PreviewAsync(CashCorrectionReplayRequest request, CancellationToken ct)
    {
        var events = await Select(request, ct);
        return new(Manifest(request, events), events.Count);
    }

    public async Task<Guid> ExecuteAsync(CashCorrectionReplayRequest request, Guid operatorId, string reason, string expectedManifest, CancellationToken ct)
    {
        if (operatorId == Guid.Empty || reason is not ("rebuild" or "retry")) throw new ArgumentException("Invalid replay attribution.");
        var events = await Select(request, ct);
        string manifest = Manifest(request, events);
        if (manifest != expectedManifest) throw new InvalidOperationException("Replay selection changed; run preview again.");
        Guid run = Guid.NewGuid();
        // Durable intent precedes the first broker call. Outcomes are append-only; missing confirmations are uncertain.
        try
        {
            await store.StartAsync(run, request, operatorId, reason, manifest, events.Count, ct);
            foreach (var value in events)
            {
                await store.AppendAsync(run, value.Id, "started", ct);
                await transport.PublishAsync(value, ct);
                await store.AppendAsync(run, value.Id, "confirmed", ct);
            }
        }
        catch (Exception) { throw new CashCorrectionReplayInterruptedException(run); }
        return run;
    }

    private async Task<IReadOnlyList<CashCorrectionReplayEvent>> Select(CashCorrectionReplayRequest request, CancellationToken ct)
    {
        request.Validate();
        var events = await store.SelectAsync(request, ct);
        if (events.Count > request.Limit) throw new InvalidOperationException("Replay selection exceeds limit; narrow the capture range.");
        foreach (var item in events)
        {
            if (Encoding.UTF8.GetByteCount(item.Payload) > 32768) throw new ArgumentException("Invalid retained event.");
            var e = JsonSerializer.Deserialize<PosLateCashCorrectionPostedV1>(item.Payload, Json) ?? throw new ArgumentException("Invalid retained event.");
            if (e.CashVarianceAdjustment >= 0 || e.Currency != "THB" || e.CorrectionId == Guid.Empty || e.OccurredAtUtc < e.PostingFromUtc || e.OccurredAtUtc >= e.PostingToUtc || e.EventId != item.Id || e.CorrelationId == Guid.Empty || e.OrganizationId != request.OrganizationId ||
                e.BranchId != request.BranchId || e.RestaurantId != request.RestaurantId || e.OccurredAtUtc < request.FromUtc || e.OccurredAtUtc >= request.ToUtc)
                throw new ArgumentException("Retained event scope mismatch.");
        }
        return events.OrderBy(x => x.Id).ToArray();
    }
    private static string Manifest(CashCorrectionReplayRequest request, IReadOnlyList<CashCorrectionReplayEvent> events) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            request.OrganizationId, request.BranchId, request.RestaurantId,
            From = request.FromUtc.ToUniversalTime(), To = request.ToUtc.ToUniversalTime(), request.Limit,
            Events = events.Select(e => new { e.Id, Hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(e.Payload))) })
        }))));
}
