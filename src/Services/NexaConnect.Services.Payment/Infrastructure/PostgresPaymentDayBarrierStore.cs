using NexaConnect.Contracts.Reporting;
using NexaConnect.Infrastructure.Persistence;
using NexaConnect.Services.Payment.Application.Refunds;
using NexaConnect.Services.Payment.Domain;
using Npgsql;
namespace NexaConnect.Services.Payment.Infrastructure;

public sealed class PostgresPaymentDayBarrierStore(NpgsqlDataSource source) : IPaymentDayBarrierStore
{
    public Task<SourceBarrierProof?> ReadAsync(EndOfDayWindow window, Guid id, CancellationToken ct) => new PostgresDayBarrier(source).ReadAsync(window, id, ct);
    public Task<SourceBarrierProof> ExecuteAsync(SourceBarrierRequest request, CancellationToken ct) =>
        new PostgresDayBarrier(source).ExecuteAsync(request, (previous, next, live, decision) =>
        {
            try { FinancialDayBarrier.ValidateTransition(previous, next, live, decision); }
            catch (InvalidOperationException) { throw new SnapshotOperationConflictException(); }
        }, ct);
}
