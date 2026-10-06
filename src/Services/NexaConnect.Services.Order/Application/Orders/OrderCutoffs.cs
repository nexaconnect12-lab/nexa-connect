using NexaConnect.Contracts.Reporting;
namespace NexaConnect.Services.Order.Application.Orders;
public interface IOrderCutoffStore
{
    Task<SourceCutoff<OrderDaySummary>> CaptureAsync(SourceCutoffCommand command, string actor, CancellationToken ct);
    Task<SourceCutoffRead<OrderDaySummary>?> ReadAsync(EndOfDayWindow window, Guid id, CancellationToken ct);
}
public sealed class OrderCutoffs(OrderDayRead authorization, IOrderCutoffStore? store = null)
{
    public async Task<SourceCutoffRead<OrderDaySummary>> CaptureAsync(SourceCutoffCommand command, string bearer, string actor, CancellationToken ct)
    {
        if (command.OperationId == Guid.Empty) throw new ArgumentException();
        await authorization.AuthorizeAsync(command.Window, bearer, ct);
        var captured = await (store ?? throw new InvalidOperationException("Durable cutoff persistence is required.")).CaptureAsync(command, actor, ct);
        return await (store ?? throw new InvalidOperationException("Durable cutoff persistence is required.")).ReadAsync(command.Window, captured.ManifestId, ct) ?? throw new InvalidOperationException();
    }
    public async Task<SourceCutoffRead<OrderDaySummary>?> ReadAsync(EndOfDayWindow window, Guid id, string bearer, CancellationToken ct)
    {
        if (id == Guid.Empty) throw new ArgumentException();
        await authorization.AuthorizeAsync(window, bearer, ct);
        return await (store ?? throw new InvalidOperationException("Durable cutoff persistence is required.")).ReadAsync(window, id, ct);
    }
}
