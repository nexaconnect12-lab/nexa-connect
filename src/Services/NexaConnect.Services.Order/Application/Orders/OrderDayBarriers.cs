using NexaConnect.Contracts.Reporting;
namespace NexaConnect.Services.Order.Application.Orders;

public interface IOrderDayBarrierStore
{
    Task<SourceBarrierProof> ExecuteAsync(SourceBarrierRequest request, CancellationToken ct);
    Task<SourceBarrierProof?> ReadAsync(EndOfDayWindow window, Guid id, CancellationToken ct);
}

public sealed class OrderDayBarriers(IOrderDayBarrierStore store)
{
    public Task<SourceBarrierProof> ExecuteAsync(SourceBarrierRequest request, CancellationToken ct) => store.ExecuteAsync(request, ct);
    public Task<SourceBarrierProof?> ReadAsync(EndOfDayWindow window, Guid id, CancellationToken ct) => store.ReadAsync(window, id, ct);
}
