namespace NexaConnect.Services.POS.Domain.Drawers;

public sealed record DrawerScope(Guid OrganizationId,Guid RestaurantId,Guid BranchId,Guid StoreId,Guid TerminalId,Guid ShiftId,Guid CashSessionId);
public sealed record DrawerCommand(DrawerScope Scope,Guid OperationId,string Cause,Guid? OrderId,string? Reason);
public sealed record DrawerCustody(string StoreStatus,string TerminalStatus,string ShiftStatus,string CashStatus,
    string Currency,string Cashier,DateTimeOffset ShiftOpened,bool ShiftClosed,DateTimeOffset CashOpened,bool CashClosed,
    bool ConfirmedCashSale,DateTimeOffset? SettledAtUtc);
public sealed class DrawerConflictException:Exception;
public sealed record DrawerIntent(DrawerCommand Command,string Actor,Guid DecisionId,DateTimeOffset ReservedAtUtc,
    DateTimeOffset ExpiresAtUtc,string Status="reserved",DateTimeOffset? ReportedAtUtc=null)
{
    public static void Validate(DrawerCommand command,string actor)
    {
        var s=command.Scope;
        if(new[]{s.OrganizationId,s.RestaurantId,s.BranchId,s.StoreId,s.TerminalId,s.ShiftId,s.CashSessionId,command.OperationId}.Any(x=>x==Guid.Empty)
            ||string.IsNullOrWhiteSpace(actor)||actor.Length>200||actor.Any(char.IsControl)
            ||command.Cause is not ("manual" or "cash_sale")
            ||command.Cause=="manual"&&(command.OrderId is not null||command.Reason is not ("change_required" or "cash_check" or "supervisor_access"))
            ||command.Cause=="cash_sale"&&(command.OrderId is null||command.OrderId==Guid.Empty||command.Reason is not null))
            throw new ArgumentException("Valid original scope, operation and fixed drawer reason are required.");
    }
    public static DrawerIntent Reserve(DrawerCommand command,string actor,Guid decision,DrawerCustody? custody,DateTimeOffset now)
    {
        Validate(command,actor);
        if(decision==Guid.Empty||custody is null||custody.StoreStatus!="active"||custody.TerminalStatus!="active"
            ||custody.ShiftStatus!="open"||custody.CashStatus!="open"||custody.Currency!="THB"||custody.Cashier!=actor
            ||custody.ShiftClosed||custody.CashClosed||custody.ShiftOpened>now||custody.CashOpened>now)
            throw new DrawerConflictException();
        if(command.Cause=="cash_sale"&&(!custody.ConfirmedCashSale||custody.SettledAtUtc is not {} settled||settled>now||settled<now.AddSeconds(-60)))
            throw new DrawerConflictException();
        return new(command,actor,decision,now,now.AddSeconds(60));
    }
    public void RequireSame(DrawerCommand command,string actor)
    {
        // A sale has one intent across retry operation IDs. Every other original fact is pinned.
        if(Actor!=actor||(Command with{OperationId=command.OperationId})!=command)throw new DrawerConflictException();
    }
    public DrawerIntent Report(string outcome,DateTimeOffset now)
    {
        if(outcome is not ("submitted" or "unknown" or "cancelled"))throw new ArgumentException("A fixed drawer outcome is required.");
        if(Status!="reserved"){if(Status!=outcome)throw new DrawerConflictException();return this;}
        return this with{Status=outcome,ReportedAtUtc=now};
    }
}
