using NexaConnect.Contracts.Reporting;
using NexaConnect.Infrastructure.Persistence;
using NexaConnect.Services.Order.Application.Orders;
using NexaConnect.Services.Order.Domain;
using Npgsql;
namespace NexaConnect.Services.Order.Infrastructure.Persistence;
public sealed class PostgresOrderDayFenceStore(NpgsqlDataSource source):IOrderDayFenceStore
{
 public Task<SourceDayFence?> ReadAsync(EndOfDayWindow window,Guid operation,CancellationToken ct)=>new PostgresWindowFence(source).ReadAsync(window,operation,ct);
 public Task<SourceDayFence> ExecuteAsync(SourceFenceCommand command,string actor,bool cancel,CancellationToken ct)=>new PostgresWindowFence(source).ExecuteAsync(command,actor,cancel,
   json=>FinancialChangeAttribution.Classify(json,command.Window.FromUtc,command.Window.ToUtc).AffectsWindow,
   (epoch,suffix,observed,valid)=>{try{FinancialDayFence.ValidateAdmission(epoch,suffix,observed,valid);}catch(InvalidOperationException){throw new SnapshotOperationConflictException();}},ct);
}
