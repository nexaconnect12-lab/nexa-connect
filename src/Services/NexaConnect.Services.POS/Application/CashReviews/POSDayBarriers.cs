using NexaConnect.Contracts.Reporting;
namespace NexaConnect.Services.POS.Application.CashReviews;

public interface IPOSDayBarrierStore
{
    Task<SourceBarrierProof> ExecuteAsync(SourceBarrierRequest request, CancellationToken ct);
    Task<SourceBarrierProof?> ReadAsync(EndOfDayWindow window, Guid id, CancellationToken ct);
}

public sealed class POSDayBarriers(IPOSDayBarrierStore store)
{
    public Task<SourceBarrierProof> ExecuteAsync(SourceBarrierRequest request, CancellationToken ct) => store.ExecuteAsync(request, ct);
    public Task<SourceBarrierProof?> ReadAsync(EndOfDayWindow window, Guid id, CancellationToken ct) => store.ReadAsync(window, id, ct);
}
