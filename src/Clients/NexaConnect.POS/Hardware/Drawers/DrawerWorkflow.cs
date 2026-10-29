namespace NexaConnect.POS.Hardware.Drawers;

public interface IDrawerDriver
{
    bool Configured { get; }
    // Success means submitted, never proof that the drawer physically opened.
    Task SubmitOpenAsync(CancellationToken cancellationToken);
}
public sealed class DisabledDrawerDriver : IDrawerDriver
{
    public bool Configured=>false;
    public Task SubmitOpenAsync(CancellationToken cancellationToken)=>throw new InvalidOperationException("Drawer integration is unavailable.");
}
public enum DrawerPermitStatus { Granted,Denied,AlreadyAttempted,Conflict,Unavailable }
public sealed record DrawerPermit(DrawerPermitStatus Status,DateTimeOffset? ExpiresAtUtc=null)
{
    public bool AllowsSubmission(DateTimeOffset now)=>Status==DrawerPermitStatus.Granted&&ExpiresAtUtc>now&&ExpiresAtUtc<=now.AddSeconds(60);
}
public interface IDrawerAuthorization
{
    Task<DrawerPermit> AuthorizeAsync(DrawerRequest request,CancellationToken cancellationToken);
    Task ReportAsync(DrawerRequest request,DrawerOutcome outcome,CancellationToken cancellationToken);
}
public interface IDrawerIntentStore
{
    DrawerReservation Reserve(DrawerRequest request);
    void Complete(DrawerRequest request,DrawerOutcome outcome);
}

// Production uses DisabledDrawerDriver until a vendor-verified adapter exists.
// A local reservation precedes HTTP: a lost server response must never invite replay.
public sealed class DrawerWorkflow(IDrawerDriver driver,IDrawerAuthorization authorization,IDrawerIntentStore intents,TimeProvider? clock=null)
{
    public bool Configured=>driver.Configured;
    public async Task<DrawerOutcome> OpenAsync(DrawerRequest request,Func<bool> sessionCurrent,CancellationToken ct)
    {
        request.Validate();
        if(!driver.Configured)return DrawerOutcome.Disabled;
        if(ct.IsCancellationRequested||!sessionCurrent())return DrawerOutcome.Cancelled;
        DrawerReservation reservation;
        try{reservation=intents.Reserve(request);}
        catch{return DrawerOutcome.AuditUnavailable;}
        if(reservation!=DrawerReservation.Created)return reservation==DrawerReservation.AlreadyAttempted?DrawerOutcome.AlreadyAttempted:DrawerOutcome.Conflict;
        if(ct.IsCancellationRequested||!sessionCurrent()){TryComplete(request,DrawerOutcome.Cancelled);return DrawerOutcome.Cancelled;}
        DrawerPermit permit;
        try{permit=await authorization.AuthorizeAsync(request,ct);}
        catch(UnauthorizedAccessException){TryComplete(request,DrawerOutcome.Cancelled);return DrawerOutcome.Denied;}
        catch{TryComplete(request,DrawerOutcome.Unknown);return DrawerOutcome.AuditUnavailable;}
        if(permit.Status!=DrawerPermitStatus.Granted){
            TryComplete(request,permit.Status==DrawerPermitStatus.Unavailable?DrawerOutcome.Unknown:DrawerOutcome.Cancelled);
            return permit.Status switch{DrawerPermitStatus.Denied=>DrawerOutcome.Denied,DrawerPermitStatus.AlreadyAttempted=>DrawerOutcome.AlreadyAttempted,DrawerPermitStatus.Conflict=>DrawerOutcome.Conflict,_=>DrawerOutcome.AuditUnavailable};
        }
        DrawerOutcome outcome;
        bool attempted=false;
        if(ct.IsCancellationRequested||!sessionCurrent()||!driver.Configured||!permit.AllowsSubmission((clock??TimeProvider.System).GetUtcNow()))outcome=DrawerOutcome.Cancelled;
        else
        {
            attempted=true;
            try{await driver.SubmitOpenAsync(ct);outcome=DrawerOutcome.Submitted;}
            catch{outcome=DrawerOutcome.Unknown;}
        }
        // Outcome reporting is bounded independently and never retries the device.
        // If identity/authority is no longer available, retain consumed uncertainty.
        try{using var reportDeadline=new CancellationTokenSource(TimeSpan.FromSeconds(5));await authorization.ReportAsync(request,outcome,reportDeadline.Token);}
        catch{TryComplete(request,DrawerOutcome.Unknown);return attempted?DrawerOutcome.AuditUnavailableAfterAttempt:DrawerOutcome.AuditUnavailable;}
        if(!TryComplete(request,outcome))return attempted?DrawerOutcome.AuditUnavailableAfterAttempt:DrawerOutcome.AuditUnavailable;
        return outcome;
    }
    private bool TryComplete(DrawerRequest request,DrawerOutcome outcome){try{intents.Complete(request,outcome);return true;}catch{return false;}}
}
