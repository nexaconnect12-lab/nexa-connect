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
public interface IDrawerAuthorization
{
    // Future server adapter must verify exact live scope/shift/custody and either
    // original confirmed CASH settlement or a manual-open permission + reason.
    Task<bool> AuthorizeAsync(DrawerRequest request,CancellationToken cancellationToken);
}
public interface IDrawerIntentStore
{
    DrawerReservation Reserve(DrawerRequest request);
    void Complete(DrawerRequest request,DrawerOutcome outcome);
}

// Not wired to production payment or manual actions until owning-service authority
// and a vendor-verified driver are available. Never retries or changes payment state.
public sealed class DrawerWorkflow(IDrawerDriver driver,IDrawerAuthorization authorization,IDrawerIntentStore intents)
{
    public async Task<DrawerOutcome> OpenAsync(DrawerRequest request,Func<bool> sessionCurrent,CancellationToken ct)
    {
        request.Validate();
        if(!driver.Configured)return DrawerOutcome.Disabled;
        if(ct.IsCancellationRequested||!sessionCurrent())return DrawerOutcome.Cancelled;
        try
        {
            if(!await authorization.AuthorizeAsync(request,ct))return DrawerOutcome.Denied;
        }
        catch(OperationCanceledException){return DrawerOutcome.Cancelled;}
        catch{return DrawerOutcome.Denied;}
        if(ct.IsCancellationRequested||!sessionCurrent())return DrawerOutcome.Cancelled;
        DrawerReservation reservation;
        try{reservation=intents.Reserve(request);}
        catch{return DrawerOutcome.AuditUnavailable;}
        if(reservation!=DrawerReservation.Created)return reservation==DrawerReservation.AlreadyAttempted?DrawerOutcome.AlreadyAttempted:DrawerOutcome.Conflict;
        DrawerOutcome outcome;
        if(ct.IsCancellationRequested||!sessionCurrent())outcome=DrawerOutcome.Cancelled;
        else
        {
            try{await driver.SubmitOpenAsync(ct);outcome=DrawerOutcome.Submitted;}
            catch{outcome=DrawerOutcome.Unknown;}
        }
        try{intents.Complete(request,outcome);}
        catch{return DrawerOutcome.AuditUnavailableAfterAttempt;}
        return outcome;
    }
}
