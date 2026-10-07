using NexaConnect.Contracts.Reporting;
namespace NexaConnect.Services.Order.Application.Orders;
public interface IOrderCutoffStore
{
    Task<SourceDaySeal> SealAsync(SourceSealCommand command,string actor,CancellationToken ct);
    Task<SourceSealRead<OrderDaySummary>?> ReadSealAsync(EndOfDayWindow window,Guid id,CancellationToken ct);
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
    public async Task<SourceSealRead<OrderDaySummary>> SealAsync(SourceSealCommand command,string bearer,string actor,CancellationToken ct)
    {
        if(command.OperationId==Guid.Empty || command.ManifestId==Guid.Empty || command.ExpectedRevision is null
            || command.ExpectedRevision.Epoch==Guid.Empty || command.ExpectedRevision.Revision<0)throw new ArgumentException();
        await authorization.AuthorizeAsync(command.Window,bearer,ct);
        var seal=await (store??throw new InvalidOperationException("Durable persistence is required.")).SealAsync(command,actor,ct);
        return await (store??throw new InvalidOperationException("Durable persistence is required.")).ReadSealAsync(command.Window,seal.SealId,ct)??throw new InvalidOperationException();
    }
    public async Task<SourceSealRead<OrderDaySummary>?> ReadSealAsync(EndOfDayWindow window,Guid id,string bearer,CancellationToken ct)
    {
        if(id==Guid.Empty)throw new ArgumentException();
        await authorization.AuthorizeAsync(window,bearer,ct);
        return await (store??throw new InvalidOperationException("Durable persistence is required.")).ReadSealAsync(window,id,ct);
    }
}
