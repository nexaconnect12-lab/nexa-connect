using NexaConnect.Infrastructure.Messaging;
using NexaConnect.Observability;
using NexaConnect.Services.POS.Application.DayClose;
namespace NexaConnect.Services.POS.Infrastructure.Messaging;

public sealed class SettlementReplayTransport(IOutboxTransport transport):ISettlementReplayTransport
{
    public async Task PublishAsync(SettlementReplayEvent value,CancellationToken ct)
    {
        var original=SettlementReplay.ReadEnvelope(value);
        using var correlation=CorrelationContext.Push(original.CorrelationId.ToString("D"));
        await transport.PublishAsync(new(value.Id,"pos.branch-day-settled.v"+value.ContractVersion,value.ContractVersion,
            "branch-day-settlement",original.SettlementId,value.Payload,original.CorrelationId.ToString("D"),original.OccurredAtUtc),ct);
    }
}
