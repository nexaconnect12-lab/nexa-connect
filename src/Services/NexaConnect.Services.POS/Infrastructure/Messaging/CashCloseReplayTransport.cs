using System.Text.Json;
using NexaConnect.Contracts.IntegrationEvents;
using NexaConnect.Infrastructure.Messaging;
using NexaConnect.Observability;
using NexaConnect.Services.POS.Application.CashReviews;

namespace NexaConnect.Services.POS.Infrastructure.Messaging;

public sealed class CashCloseReplayTransport(IOutboxTransport transport) : ICashCloseReplayTransport
{
    public async Task PublishAsync(CashCloseReplayEvent value, CancellationToken ct)
    {
        var e = JsonSerializer.Deserialize<PosCashCloseSnapshotV1>(value.Payload)!;
        using var correlation = CorrelationContext.Push(e.CorrelationId.ToString("D"));
        await transport.PublishAsync(new(value.Id, "pos.cash-close.snapshot.v1", 1, "cash-session", e.CashSessionId,
            value.Payload, e.CorrelationId.ToString("D"), e.OccurredAtUtc), ct);
    }
}
