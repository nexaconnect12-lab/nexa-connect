using NexaConnect.Contracts.Reporting;
using NexaConnect.Infrastructure.Persistence;
using NexaConnect.Services.POS.Application.CashReviews;
using NexaConnect.Services.POS.Domain;
using Npgsql;
namespace NexaConnect.Services.POS.Infrastructure.Persistence;

public sealed class PostgresPOSDayBarrierStore(NpgsqlDataSource source) : IPOSDayBarrierStore
{
    public Task<SourceBarrierProof?> ReadAsync(EndOfDayWindow window, Guid id, CancellationToken ct) => new PostgresDayBarrier(source).ReadAsync(window, id, ct);
    public Task<SourceBarrierProof> ExecuteAsync(SourceBarrierRequest request, CancellationToken ct) =>
        new PostgresDayBarrier(source).ExecuteAsync(request, (previous, next, live, decision) =>
        {
            try { FinancialDayBarrier.ValidateTransition(previous, next, live, decision); }
            catch (InvalidOperationException) { throw new SnapshotOperationConflictException(); }
        }, ct);
}
