using NexaConnect.POS.Hardware.Drawers;

namespace NexaConnect.UnitTests;

public sealed class DrawerWorkflowTests : IDisposable
{
    private readonly string directory=Path.Combine(Path.GetTempPath(),"nexa-drawer-test-"+Guid.NewGuid().ToString("N"));
    private SqliteDrawerIntentStore Store()=>new(Path.Combine(directory,"intent.sqlite"));
    private static DrawerRequest Sale()=>new(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),"test-cashier",Guid.NewGuid(),DrawerCause.ConfirmedCashSale,Guid.NewGuid());
    public void Dispose(){if(Directory.Exists(directory))Directory.Delete(directory,true);}

    [Fact] public async Task Disabled_driver_never_checks_authority_or_creates_evidence()
    {
        var auth=new Authority{Throw=true};var workflow=new DrawerWorkflow(new DisabledDrawerDriver(),auth,Store());
        Assert.Equal(DrawerOutcome.Disabled,await workflow.OpenAsync(Sale(),()=>true,default));
        Assert.Equal(0,auth.Calls);Assert.False(Directory.Exists(directory));
    }
    [Fact] public async Task Denied_authority_consumes_local_intent_without_opening()
    {
        var driver=new Driver();var auth=new Authority{Allowed=false};
        Assert.Equal(DrawerOutcome.Denied,await new DrawerWorkflow(driver,auth,Store()).OpenAsync(Sale(),()=>true,default));
        Assert.Equal(0,driver.Calls);Assert.True(Directory.Exists(directory));
    }
    [Fact] public async Task Authority_failure_fails_closed()
    {
        var driver=new Driver();
        Assert.Equal(DrawerOutcome.AuditUnavailable,await new DrawerWorkflow(driver,new Authority{Throw=true},Store()).OpenAsync(Sale(),()=>true,default));
        Assert.Equal(0,driver.Calls);
    }
    [Fact] public async Task Lock_during_authorization_consumes_intent_without_opening()
    {
        bool current=true;var driver=new Driver();var auth=new Authority{After=()=>current=false};
        Assert.Equal(DrawerOutcome.Cancelled,await new DrawerWorkflow(driver,auth,Store()).OpenAsync(Sale(),()=>current,default));
        Assert.Equal(0,driver.Calls);Assert.True(Directory.Exists(directory));
    }
    [Fact] public async Task Cancelled_before_authorization_has_no_side_effects()
    {
        var driver=new Driver();var auth=new Authority();using var ct=new CancellationTokenSource();ct.Cancel();
        Assert.Equal(DrawerOutcome.Cancelled,await new DrawerWorkflow(driver,auth,Store()).OpenAsync(Sale(),()=>true,ct.Token));
        Assert.Equal(0,auth.Calls);Assert.Equal(0,driver.Calls);
    }
    [Fact] public async Task Reserved_before_submission_and_original_sale_cannot_repeat_after_restart()
    {
        var sale=Sale();var store=Store();var driver=new Driver{Before=()=>Assert.Equal(DrawerReservation.AlreadyAttempted,Store().Reserve(sale))};
        Assert.Equal(DrawerOutcome.Submitted,await new DrawerWorkflow(driver,new Authority(),store).OpenAsync(sale,()=>true,default));
        Assert.Equal(DrawerOutcome.AlreadyAttempted,await new DrawerWorkflow(driver,new Authority(),Store()).OpenAsync(sale with{OperationId=Guid.NewGuid()},()=>true,default));
        Assert.Equal(1,driver.Calls);
    }
    [Fact] public async Task Crashed_reserved_intent_is_not_replayed()
    {
        var sale=Sale();Assert.Equal(DrawerReservation.Created,Store().Reserve(sale));var driver=new Driver();
        Assert.Equal(DrawerOutcome.AlreadyAttempted,await new DrawerWorkflow(driver,new Authority(),Store()).OpenAsync(sale,()=>true,default));
        Assert.Equal(0,driver.Calls);
    }
    [Fact] public async Task Peripheral_failure_remains_unknown_and_cannot_blindly_retry()
    {
        var sale=Sale();var driver=new Driver{Throw=true};var workflow=new DrawerWorkflow(driver,new Authority(),Store());
        Assert.Equal(DrawerOutcome.Unknown,await workflow.OpenAsync(sale,()=>true,default));
        Assert.Equal(DrawerOutcome.AlreadyAttempted,await workflow.OpenAsync(sale,()=>true,default));Assert.Equal(1,driver.Calls);
    }
    [Fact] public async Task Changed_actor_or_custody_conflicts_without_second_submission()
    {
        var sale=Sale();var driver=new Driver();var workflow=new DrawerWorkflow(driver,new Authority(),Store());
        Assert.Equal(DrawerOutcome.Submitted,await workflow.OpenAsync(sale,()=>true,default));
        foreach(var altered in new[]{sale with{Subject="another"},sale with{ShiftId=Guid.NewGuid()},sale with{CashSessionId=Guid.NewGuid()}})
            Assert.Equal(DrawerOutcome.Conflict,await workflow.OpenAsync(altered,()=>true,default));
        Assert.Equal(1,driver.Calls);
    }
    [Fact] public async Task Audit_failure_before_open_prevents_driver_call()
    {
        var driver=new Driver();
        Assert.Equal(DrawerOutcome.AuditUnavailable,await new DrawerWorkflow(driver,new Authority(),new BrokenStore()).OpenAsync(Sale(),()=>true,default));
        Assert.Equal(0,driver.Calls);
    }
    [Fact] public async Task Lock_after_reservation_consumes_intent_without_opening()
    {
        var request=Sale();bool current=true;var driver=new Driver();
        var workflow=new DrawerWorkflow(driver,new Authority(),new HookStore(Store(),()=>current=false));
        Assert.Equal(DrawerOutcome.Cancelled,await workflow.OpenAsync(request,()=>current,default));
        Assert.Equal(0,driver.Calls);
        Assert.Equal(DrawerReservation.AlreadyAttempted,Store().Reserve(request));
    }
    [Fact] public void Full_scope_is_part_of_intent_identity()
    {
        var request=Sale();
        foreach(var other in new[]{request with{OrganizationId=Guid.NewGuid()},request with{RestaurantId=Guid.NewGuid()},request with{BranchId=Guid.NewGuid()},request with{StoreId=Guid.NewGuid()},request with{TerminalId=Guid.NewGuid()}})
        {Assert.NotEqual(request.Key,other.Key);Assert.NotEqual(request.Fingerprint,other.Fingerprint);}
    }
    [Fact] public async Task Audit_completion_failure_does_not_repeat_submission()
    {
        var sale=Sale();var driver=new Driver();var store=new BrokenStore{Inner=Store()};var workflow=new DrawerWorkflow(driver,new Authority(),store);
        Assert.Equal(DrawerOutcome.AuditUnavailableAfterAttempt,await workflow.OpenAsync(sale,()=>true,default));
        Assert.Equal(DrawerOutcome.AlreadyAttempted,await workflow.OpenAsync(sale,()=>true,default));Assert.Equal(1,driver.Calls);
    }
    [Fact] public async Task Concurrent_original_sale_submits_at_most_once()
    {
        var sale=Sale();var driver=new Driver();
        var outcomes=await Task.WhenAll(Enumerable.Range(0,8).Select(_=>Task.Run(()=>new DrawerWorkflow(driver,new Authority(),Store()).OpenAsync(sale,()=>true,default))));
        Assert.Single(outcomes,x=>x==DrawerOutcome.Submitted);Assert.Equal(1,driver.Calls);
        Assert.All(outcomes,x=>Assert.Contains(x,new[]{DrawerOutcome.Submitted,DrawerOutcome.AlreadyAttempted}));
    }
    [Fact] public void Manual_reason_and_original_sale_shape_are_required()
    {
        var sale=Sale();
        foreach(var invalid in new[]{sale with{OrderId=null},sale with{Reason=DrawerReason.CashCheck},sale with{Cause=DrawerCause.Manual},sale with{Subject="bad\nactor"},sale with{StoreId=Guid.Empty},sale with{Cause=(DrawerCause)99}})
            Assert.Throws<ArgumentException>(invalid.Validate);
        (sale with{Cause=DrawerCause.Manual,OrderId=null,Reason=DrawerReason.CashCheck}).Validate();
    }
    [Fact] public async Task Manual_request_uses_explicit_reason_and_original_operation()
    {
        var request=Sale() with{Cause=DrawerCause.Manual,OrderId=null,Reason=DrawerReason.ChangeRequired};var driver=new Driver();var auth=new Authority();
        var workflow=new DrawerWorkflow(driver,auth,Store());
        Assert.Equal(DrawerOutcome.Submitted,await workflow.OpenAsync(request,()=>true,default));
        Assert.Equal(request,auth.Last);Assert.Equal(DrawerOutcome.Conflict,await workflow.OpenAsync(request with{Reason=DrawerReason.CashCheck},()=>true,default));
        Assert.Equal(1,driver.Calls);
    }
    [Fact] public void Evidence_contains_no_raw_subject_or_financial_identity()
    {
        var request=Sale();Store().Reserve(request);
        string bytes=System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(directory,"intent.sqlite")));
        Assert.DoesNotContain(request.Subject,bytes);Assert.DoesNotContain(request.OrderId!.Value.ToString(),bytes);
        Assert.DoesNotContain(request.TerminalId.ToString(),bytes);
    }
    private sealed class Driver:IDrawerDriver
    {
        public bool Configured=>true;public int Calls;public bool Throw;public Action? Before;
        public Task SubmitOpenAsync(CancellationToken ct){Interlocked.Increment(ref Calls);Before?.Invoke();if(Throw)throw new IOException("test failure");return Task.CompletedTask;}
    }
    private sealed class Authority:IDrawerAuthorization
    {
        public bool Allowed=true,Throw;public int Calls;public Action? After;public DrawerRequest? Last;
        public Task<DrawerPermit> AuthorizeAsync(DrawerRequest request,CancellationToken ct){Calls++;Last=request;After?.Invoke();if(Throw)throw new IOException();return Task.FromResult(new DrawerPermit(Allowed?DrawerPermitStatus.Granted:DrawerPermitStatus.Denied,DateTimeOffset.UtcNow.AddSeconds(60)));}
        public Task ReportAsync(DrawerRequest request,DrawerOutcome outcome,CancellationToken ct)=>Task.CompletedTask;
    }
    private sealed class BrokenStore:IDrawerIntentStore
    {
        public IDrawerIntentStore? Inner;
        public DrawerReservation Reserve(DrawerRequest r)=>Inner?.Reserve(r)??throw new IOException();
        public void Complete(DrawerRequest r,DrawerOutcome outcome)=>throw new IOException();
    }
    private sealed class HookStore(IDrawerIntentStore inner,Action after):IDrawerIntentStore
    {
        public DrawerReservation Reserve(DrawerRequest request){var result=inner.Reserve(request);after();return result;}
        public void Complete(DrawerRequest request,DrawerOutcome outcome)=>inner.Complete(request,outcome);
    }
}
