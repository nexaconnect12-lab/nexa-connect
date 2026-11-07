using NexaConnect.Contracts.Reporting;
using NexaConnect.Infrastructure.Persistence;
using NexaConnect.Services.POS.Application.CashReviews;
using NexaConnect.Services.POS.Domain;
using Npgsql;
namespace NexaConnect.Services.POS.Infrastructure.Persistence;
public sealed class PostgresPosDayFenceStore(NpgsqlDataSource source):IPosDayFenceStore
{
 public Task<SourceDayFence?> ReadAsync(EndOfDayWindow window,Guid operation,CancellationToken ct)=>new PostgresWindowFence(source).ReadAsync(window,operation,ct);
 public async Task<SourceDayFence> ExecuteAsync(SourceFenceCommand command,string actor,bool cancel,CancellationToken ct)
 {
   if (!cancel)
   {
     await using var connection=await source.OpenConnectionAsync(ct);
     await using var transaction=await connection.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead,ct);
     var manual=await PostgresManualRefundDayEvidence.ReadAsync(command.Window,connection,transaction,ct);
     if (manual is { CompletedCount: >0 } or { UnresolvedPayouts: >0 }) throw new SnapshotOperationConflictException();
     await transaction.CommitAsync(ct);
   }
   return await new PostgresWindowFence(source).ExecuteAsync(command,actor,cancel,
     json=>FinancialChangeAttribution.Classify(json,command.Window.FromUtc,command.Window.ToUtc).AffectsWindow,
     (epoch,suffix,observed,valid)=>{try{FinancialDayFence.ValidateAdmission(epoch,suffix,observed,valid);}catch(InvalidOperationException){throw new SnapshotOperationConflictException();}},ct);
 }
}
