using NexaConnect.Contracts.Reporting;
using NexaConnect.Infrastructure.Persistence;
using NexaConnect.Services.Order.Application.Orders;
using NexaConnect.Services.Order.Domain;
using Npgsql;
namespace NexaConnect.Services.Order.Infrastructure.Persistence;

public sealed class PostgresOrderDayBarrierStore(NpgsqlDataSource source) : IOrderDayBarrierStore
{
    public Task<SourceBarrierProof?> ReadAsync(EndOfDayWindow window, Guid id, CancellationToken ct) => new PostgresDayBarrier(source).ReadAsync(window, id, ct);
    public Task<SourceBarrierProof> ExecuteAsync(SourceBarrierRequest request, CancellationToken ct) =>
        new PostgresDayBarrier(source).ExecuteAsync(request, (previous, next, live, decision) =>
        {
            try { FinancialDayBarrier.ValidateTransition(previous, next, live, decision); }
            catch (InvalidOperationException) { throw new SnapshotOperationConflictException(); }
        }, ct);
}
