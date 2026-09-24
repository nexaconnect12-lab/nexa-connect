using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NexaConnect.Contracts.IntegrationEvents;

namespace NexaConnect.Services.POS.Application.CashReviews;

public sealed record CashCloseReplayRequest(Guid OrganizationId, Guid BranchId, Guid StoreId,
    DateTimeOffset FromUtc, DateTimeOffset ToUtc, int Limit)
{
    public void Validate()
    {
        if (OrganizationId == Guid.Empty || BranchId == Guid.Empty || StoreId == Guid.Empty ||
            FromUtc >= ToUtc || ToUtc - FromUtc > TimeSpan.FromDays(31) || Limit is < 1 or > 1000)
            throw new ArgumentException("Invalid replay scope, capture range or limit.");
    }
}
public sealed record CashCloseReplayEvent(Guid Id, string Payload)
{
    public override string ToString() => "CashCloseReplayEvent [redacted]";
}
public sealed record CashCloseReplayPlan(string Manifest, int Count);
public sealed class CashCloseReplayInterruptedException(Guid runId) : Exception("Replay interrupted; inspect its durable audit.")
{
    public Guid RunId { get; } = runId;
}
public interface ICashCloseReplayStore
{
    Task<IReadOnlyList<CashCloseReplayEvent>> SelectAsync(CashCloseReplayRequest request, CancellationToken ct);
    Task StartAsync(Guid runId, CashCloseReplayRequest request, Guid operatorId, string reason, string manifest, int count, CancellationToken ct);
    Task AppendAsync(Guid runId, Guid eventId, string outcome, CancellationToken ct);
}
public interface ICashCloseReplayTransport
{
    Task PublishAsync(CashCloseReplayEvent value, CancellationToken ct);
}

public sealed class CashCloseReplay(ICashCloseReplayStore store, ICashCloseReplayTransport transport)
{
    public async Task<CashCloseReplayPlan> PreviewAsync(CashCloseReplayRequest request, CancellationToken ct)
    {
        var events = await Select(request, ct);
        return new(Manifest(request, events), events.Count);
    }

    public async Task<Guid> ExecuteAsync(CashCloseReplayRequest request, Guid operatorId, string reason, string expectedManifest, CancellationToken ct)
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
        catch (Exception) { throw new CashCloseReplayInterruptedException(run); }
        return run;
    }

    private async Task<IReadOnlyList<CashCloseReplayEvent>> Select(CashCloseReplayRequest request, CancellationToken ct)
    {
        request.Validate();
        var events = await store.SelectAsync(request, ct);
        if (events.Count > request.Limit) throw new InvalidOperationException("Replay selection exceeds limit; narrow the capture range.");
        foreach (var item in events)
        {
            if (Encoding.UTF8.GetByteCount(item.Payload) > 32768) throw new ArgumentException("Invalid retained event.");
            var e = JsonSerializer.Deserialize<PosCashCloseSnapshotV1>(item.Payload) ?? throw new ArgumentException("Invalid retained event.");
            if (e.EventId != item.Id || e.CorrelationId == Guid.Empty || e.OrganizationId != request.OrganizationId ||
                e.BranchId != request.BranchId || e.StoreId != request.StoreId || e.OccurredAtUtc < request.FromUtc || e.OccurredAtUtc >= request.ToUtc)
                throw new ArgumentException("Retained event scope mismatch.");
        }
        return events.OrderBy(x => x.Id).ToArray();
    }
    private static string Manifest(CashCloseReplayRequest request, IReadOnlyList<CashCloseReplayEvent> events) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            request.OrganizationId, request.BranchId, request.StoreId,
            From = request.FromUtc.ToUniversalTime(), To = request.ToUtc.ToUniversalTime(), request.Limit,
            Events = events.Select(e => new { e.Id, Hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(e.Payload))) })
        }))));
}
