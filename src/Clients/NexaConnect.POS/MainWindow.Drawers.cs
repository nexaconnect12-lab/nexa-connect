using System.Windows;
using NexaConnect.POS.Hardware.Drawers;
namespace NexaConnect.POS;

public partial class MainWindow
{
    private readonly DrawerCoordinator drawerCoordinator;
    private bool drawerInFlight;
    private bool DrawerSessionCurrent(int generation,string? subject,Guid? shift,Guid? cash)=>PrintSessionCurrent(generation,subject)&&_activeShift?.ShiftId==shift&&cashSessionId==cash&&shift is not null&&cash is not null;
    private DrawerRequest DrawerRequestFor(string subject,Guid shift,Guid cash,Guid operation,DrawerCause cause,Guid? order=null,DrawerReason? reason=null)=>
        new(_configuration.OrganizationId,_configuration.RestaurantId,_configuration.BranchId,_configuration.StoreId,_configuration.TerminalId,shift,cash,subject,operation,cause,order,reason);
    private void RenderDrawerControls()
    {
        if(!viewInitialized)return;
        bool eligible=drawerCoordinator.Configured&&IsSignedIn()&&!busy&&!drawerInFlight&&!settlementInFlight&&!settlementUncertain&&!displaySafetyBlocked
            &&_activeShift is not null&&cashSessionId is not null&&pendingRefund is null&&pendingCheckout is null&&pendingOrder is null&&pendingCashReviewAttempt is null;
        DrawerOpenButton.IsEnabled=eligible;DrawerReasonPicker.IsEnabled=eligible;
    }
    private async void DrawerOpen_Click(object sender,RoutedEventArgs e)
    {
        if(!DrawerOpenButton.IsEnabled||drawerInFlight||!drawerCoordinator.Configured||_authentication.CurrentToken?.Subject is not {} subject||_activeShift is null||cashSessionId is null)return;
        int generation=receiptSessionGeneration;var shift=_activeShift.ShiftId;var cash=cashSessionId.Value;
        var reason=(DrawerReason)DrawerReasonPicker.SelectedIndex;
        if(!Enum.IsDefined(reason)||!ConfirmStaffAction("Request one manual drawer opening for the selected reason? Live permission is required.","Cash drawer"))return;
        if(!DrawerSessionCurrent(generation,subject,shift,cash))return;
        drawerInFlight=true;SetBusy("Requesting drawer authority…");
        try{
            var result=await drawerCoordinator.ManualAsync(DrawerRequestFor(subject,shift,cash,Guid.NewGuid(),DrawerCause.Manual,reason:reason),true,
                ()=>DrawerSessionCurrent(generation,subject,shift,cash)&&pendingCheckout is null&&pendingOrder is null&&pendingRefund is null&&pendingCashReviewAttempt is null&&!settlementUncertain&&!settlementInFlight,windowCancellation.Token);
            if(DrawerSessionCurrent(generation,subject,shift,cash))DrawerOutcomeText.Text=DrawerOutcomeMessage(result);
        }finally{drawerInFlight=false;if(!windowCancellation.IsCancellationRequested)UpdateOperationalState();}
    }
    private async Task HandleCashDrawerAfterPaymentAsync(ManualTenderResult result,LocalPendingSettlementState attempt,bool originalRecovery,int generation,string? subject,Guid? shift,Guid? cash)
    {
        // Financial success/recovery is complete before this independent best-effort action.
        if(!drawerCoordinator.Configured||subject is null||shift is null||cash is null||!DrawerSessionCurrent(generation,subject,shift,cash))return;
        drawerInFlight=true;
        try{
            var outcome=await drawerCoordinator.AfterPaymentAsync(result,attempt,DrawerRequestFor(subject,shift.Value,cash.Value,attempt.IdempotencyKey,DrawerCause.ConfirmedCashSale,result.OrderId),originalRecovery,
                ()=>DrawerSessionCurrent(generation,subject,shift,cash),windowCancellation.Token);
            if(DrawerSessionCurrent(generation,subject,shift,cash))DrawerOutcomeText.Text=DrawerOutcomeMessage(outcome);
        }catch{if(DrawerSessionCurrent(generation,subject,shift,cash))DrawerOutcomeText.Text="Drawer result unavailable. Payment remains confirmed; do not collect again or retry the device.";}
        finally{drawerInFlight=false;}
    }
    private static string DrawerOutcomeMessage(DrawerOutcome result)=>result switch{
        DrawerOutcome.Submitted=>"Drawer command submitted; physical opening is not confirmed.",
        DrawerOutcome.Disabled=>"Physical opening is disabled until verified hardware is configured.",
        DrawerOutcome.Denied=>"Drawer authority denied. The original cashier and live branch permission are required.",
        DrawerOutcome.AlreadyAttempted=>"Original drawer intent is consumed. Do not repeat the command.",
        DrawerOutcome.Cancelled=>"Drawer request cancelled, expired or ineligible. No new command was submitted.",
        DrawerOutcome.Conflict=>"Original drawer facts or custody changed. Reconcile without repeating the command.",
        _=>"Drawer result/audit is uncertain. Preserve the original intent and do not retry the device. Payment is unchanged."};
}
