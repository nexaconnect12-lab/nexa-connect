namespace NexaConnect.POS;

public enum CashierDestination { None, Checkout, Payment, ShiftCash, Terminal, CashReview, Receipts }

public sealed record CashierWorkflowInput(bool SignedIn, bool ShiftOpen = false, bool CashOpen = false,
    bool CashRequired = false, bool CheckoutPending = false, bool PaymentPending = false,
    bool PaymentUncertain = false, bool MovementsPending = false, bool ReviewPending = false, bool RefundPending = false,
    string? CancellationStatus = null);

public sealed record CashierWorkflowHint(string Title, string Description, CashierDestination Destination, string Action, bool Attention);

// Navigation guidance only. Never authorizes or invokes a command, or infers a paid outcome.
public static class CashierWorkflow
{
    public static CashierWorkflowHint Resolve(CashierWorkflowInput state) => state switch
    {
        { SignedIn: false } => new("Sign in to continue", "Your saved shift and payment work are retained.", CashierDestination.None, "", true),
        { CancellationStatus: "pending" } => new("Verify the original cancellation", "Check the saved cancellation request. Do not collect or replace the order.", CashierDestination.Payment, "Open Payment", true),
        { CancellationStatus: "blocked" } => new("Reconcile the original order", "Cancellation needs review. Preserve the original order and do not collect again.", CashierDestination.Payment, "Open Payment", true),
        { PaymentPending: true, PaymentUncertain: true } => new("Verify the previous payment", "Do not collect again. Check the original payment attempt.", CashierDestination.Payment, "Open Payment", true),
        { PaymentPending: true } => new("Complete the current payment", "Review the exact amount and tender before confirming receipt.", CashierDestination.Payment, "Open Payment", true),
        { CheckoutPending: true } => new("Verify the original order", "Continue the saved order; do not create a replacement.", CashierDestination.Checkout, "Open Checkout", true),
        { ReviewPending: true } => new("Verify the cash-review decision", "Load Cash review to reconcile the saved decision.", CashierDestination.CashReview, "Open Cash review", true),
        { RefundPending: true } => new("Verify the original refund", "Load the saved refund before attempting another.", CashierDestination.Receipts, "Open Receipts", true),
        { ShiftOpen: false } => new("Open your shift", "Start in Shift & cash before selling or collecting a QR order.", CashierDestination.ShiftCash, "Open Shift & cash", true),
        { MovementsPending: true } => new("Cash movements need attention", "Review pending or rejected movements. Opening sync does not replay them.", CashierDestination.Terminal, "Open Terminal & sync", true),
        { CashRequired: true, CashOpen: false } => new("Open the cash session", "Enter the opening float in Shift & cash before sending a cash order.", CashierDestination.ShiftCash, "Open Shift & cash", true),
        _ => new("Ready to sell", "Choose menu items or browse Orders to collect an accepted QR order.", CashierDestination.Checkout, "Open Checkout", false)
    };

    public static (string Label, string Value) Amount(string cartEstimate, bool checkoutPending, decimal? unpaidAmount, string currency)
        => unpaidAmount is { } amount ? ("Unpaid order · server total", CashierPresentation.Money(amount, currency))
            : checkoutPending ? ("Original order pending", "Verify original order")
            : ("Cart estimate · review server pricing", cartEstimate);

    public static string MenuEmptyMessage(bool signedIn, bool shiftOpen, bool hasMenu, bool busy) =>
        !signedIn ? "Sign in to browse the menu. Your saved work is retained."
        : !shiftOpen ? "Open a shift in Shift & cash, then refresh the menu."
        : busy ? "Please wait for the current action to finish."
        : hasMenu ? "No matching items. Choose Clear filters to see the menu again."
        : "No menu items loaded. Choose Refresh menu to retry; ask your manager if the menu stays empty.";
}
