using System.Text.Json;
using NexaConnect.Contracts.IntegrationEvents;
using NexaConnect.Infrastructure.Messaging;
using NexaConnect.Observability;
using NexaConnect.Services.POS.Application.DayClose;

namespace NexaConnect.Services.POS.Infrastructure.Messaging;

public sealed class CashCorrectionReplayTransport(IOutboxTransport transport) : ICashCorrectionReplayTransport
{
    public async Task PublishAsync(CashCorrectionReplayEvent value, CancellationToken ct)
    {
        var e = JsonSerializer.Deserialize<PosLateCashCorrectionPostedV1>(value.Payload, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        using var correlation = CorrelationContext.Push(e.CorrelationId.ToString("D"));
        await transport.PublishAsync(new(value.Id, "pos.late-cash-correction-posted.v1", 1, "late-cash-correction", e.CorrectionId,
            value.Payload, e.CorrelationId.ToString("D"), e.OccurredAtUtc), ct);
    }
}
