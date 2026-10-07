using NexaConnect.Contracts.Reporting;
namespace NexaConnect.Services.Payment.Application.Refunds;
public interface IPaymentCutoffStore
{
    Task<SourceDaySeal> SealAsync(SourceSealCommand command,string actor,CancellationToken ct);
    Task<SourceSealRead<PaymentDaySummary>?> ReadSealAsync(EndOfDayWindow window,Guid id,CancellationToken ct);
    Task<SourceCutoff<PaymentDaySummary>> CaptureAsync(SourceCutoffCommand command, string actor, CancellationToken ct);
    Task<SourceCutoffRead<PaymentDaySummary>?> ReadAsync(EndOfDayWindow window, Guid id, CancellationToken ct);
}
public sealed class PaymentCutoffs(PaymentDayRead authorization, IPaymentCutoffStore? store = null)
{
    public async Task<SourceCutoffRead<PaymentDaySummary>> CaptureAsync(SourceCutoffCommand command, string bearer, string actor, CancellationToken ct)
    {
        if (command.OperationId == Guid.Empty) throw new ArgumentException();
        await authorization.AuthorizeAsync(command.Window, bearer, ct);
        var captured = await (store ?? throw new InvalidOperationException("Durable cutoff persistence is required.")).CaptureAsync(command, actor, ct);
        return await (store ?? throw new InvalidOperationException("Durable cutoff persistence is required.")).ReadAsync(command.Window, captured.ManifestId, ct) ?? throw new InvalidOperationException();
    }
    public async Task<SourceCutoffRead<PaymentDaySummary>?> ReadAsync(EndOfDayWindow window, Guid id, string bearer, CancellationToken ct)
    {
        if (id == Guid.Empty) throw new ArgumentException();
        await authorization.AuthorizeAsync(window, bearer, ct);
        return await (store ?? throw new InvalidOperationException("Durable cutoff persistence is required.")).ReadAsync(window, id, ct);
    }
    public async Task<SourceSealRead<PaymentDaySummary>> SealAsync(SourceSealCommand command,string bearer,string actor,CancellationToken ct)
    {
        if(command.OperationId==Guid.Empty || command.ManifestId==Guid.Empty || command.ExpectedRevision is null
            || command.ExpectedRevision.Epoch==Guid.Empty || command.ExpectedRevision.Revision<0)throw new ArgumentException();
        await authorization.AuthorizeAsync(command.Window,bearer,ct);
        var seal=await (store??throw new InvalidOperationException("Durable persistence is required.")).SealAsync(command,actor,ct);
        return await (store??throw new InvalidOperationException("Durable persistence is required.")).ReadSealAsync(command.Window,seal.SealId,ct)??throw new InvalidOperationException();
    }
    public async Task<SourceSealRead<PaymentDaySummary>?> ReadSealAsync(EndOfDayWindow window,Guid id,string bearer,CancellationToken ct)
    {
        if(id==Guid.Empty)throw new ArgumentException();
        await authorization.AuthorizeAsync(window,bearer,ct);
        return await (store??throw new InvalidOperationException("Durable persistence is required.")).ReadSealAsync(window,id,ct);
    }
}
