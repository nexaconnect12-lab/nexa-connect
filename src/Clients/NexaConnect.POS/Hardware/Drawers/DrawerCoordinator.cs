using System.IO;
namespace NexaConnect.POS.Hardware.Drawers;

// Selection of a real driver is deliberately absent. Tests inject fakes; production
// always constructs DisabledDrawerDriver. Receipt-printer preferences cannot enable it.
public sealed class DrawerCoordinator(DrawerWorkflow workflow,IDisposable? lifetime=null):IDisposable
{
    public bool Configured{get{try{return workflow.Configured;}catch{return false;}}}
    public static DrawerCoordinator Disabled(PosClientConfiguration c,Func<PosTokenSet?> token)
    {
        var authority=new HttpDrawerAuthority(c,token);
        var store=new SqliteDrawerIntentStore(Path.Combine(c.GetStorageDirectory(),"drawer-intents.db"),c.Authority);
        return new(new(new DisabledDrawerDriver(),authority,store),authority);
    }
    public Task<DrawerOutcome> AfterPaymentAsync(ManualTenderResult result,LocalPendingSettlementState attempt,DrawerRequest request,bool originalRecovery,Func<bool> sessionCurrent,CancellationToken ct)
    {
        if(!Configured)return Task.FromResult(DrawerOutcome.Disabled);
        if(originalRecovery||result.Replayed||result.Status!="Paid"||result.Method!="cash"||attempt.Method!="cash"||result.OrderId!=attempt.OrderId||request.OrderId!=result.OrderId
            ||request.OperationId!=attempt.IdempotencyKey||request.Cause!=DrawerCause.ConfirmedCashSale||result.Currency!="THB"||attempt.Currency!="THB"||result.Amount<=0||result.Amount!=attempt.Amount
            ||result.OccurredAtUtc>DateTimeOffset.UtcNow||result.OccurredAtUtc<DateTimeOffset.UtcNow.AddSeconds(-60))return Task.FromResult(DrawerOutcome.Cancelled);
        return Execute(request,sessionCurrent,ct);
    }
    public Task<DrawerOutcome> ManualAsync(DrawerRequest request,bool confirmed,Func<bool> sessionCurrent,CancellationToken ct)
    {
        if(!Configured)return Task.FromResult(DrawerOutcome.Disabled);
        if(!confirmed||request.Cause!=DrawerCause.Manual)return Task.FromResult(DrawerOutcome.Cancelled);
        return Execute(request,sessionCurrent,ct);
    }
    private async Task<DrawerOutcome> Execute(DrawerRequest request,Func<bool> sessionCurrent,CancellationToken ct)
    {try{return await workflow.OpenAsync(request,sessionCurrent,ct);}catch{return DrawerOutcome.AuditUnavailable;}}
    public void Dispose()=>lifetime?.Dispose();
}
