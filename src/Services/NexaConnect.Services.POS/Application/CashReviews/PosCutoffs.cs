using NexaConnect.Contracts.Reporting;
using NexaConnect.Services.POS.Application.Shifts;
namespace NexaConnect.Services.POS.Application.CashReviews;
public interface IPosCutoffStore
{
    Task<SourceCutoff<PosDaySummary>> CaptureAsync(SourceCutoffCommand command, string actor, CancellationToken ct);
    Task<SourceCutoffRead<PosDaySummary>?> ReadAsync(EndOfDayWindow window, Guid id, CancellationToken ct);
}
public sealed class PosCutoffs(PosDayRead authorization, IPosCutoffStore store)
{
    public async Task<SourceCutoffRead<PosDaySummary>> CaptureAsync(SourceCutoffCommand command, PosUserContext user, string actor, CancellationToken ct)
    {
        if (command.OperationId == Guid.Empty) throw new ArgumentException();
        await authorization.AuthorizeAsync(command.Window, user, ct);
        var captured = await store.CaptureAsync(command, actor, ct);
        return await store.ReadAsync(command.Window, captured.ManifestId, ct) ?? throw new InvalidOperationException();
    }
    public async Task<SourceCutoffRead<PosDaySummary>?> ReadAsync(EndOfDayWindow window, Guid id, PosUserContext user, CancellationToken ct)
    {
        if (id == Guid.Empty) throw new ArgumentException();
        await authorization.AuthorizeAsync(window, user, ct);
        return await store.ReadAsync(window, id, ct);
    }
}
