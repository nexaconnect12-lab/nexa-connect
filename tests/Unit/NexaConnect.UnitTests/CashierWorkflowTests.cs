using NexaConnect.POS;

namespace NexaConnect.UnitTests;

public class CashierWorkflowTests
{
    [Fact]
    public void Signed_out_state_never_offers_operational_navigation()
    {
        var hint = CashierWorkflow.Resolve(new(false, PaymentPending: true, CheckoutPending: true));
        Assert.Equal(CashierDestination.None, hint.Destination);
        Assert.Empty(hint.Action);
    }

    [Fact]
    public void Uncertain_payment_takes_priority_over_shift_cash_checkout_and_reviews()
    {
        var hint = CashierWorkflow.Resolve(new(true, CheckoutPending: true, PaymentPending: true, PaymentUncertain: true, ReviewPending: true));
        Assert.Equal(CashierDestination.Payment, hint.Destination);
        Assert.Contains("Do not collect again", hint.Description);
        Assert.True(hint.Attention);
    }

    [Fact]
    public void Checkout_recovery_takes_priority_over_new_shift_and_movements()
        => Assert.Equal(CashierDestination.Checkout, CashierWorkflow.Resolve(new(true, CheckoutPending: true, MovementsPending: true)).Destination);

    [Theory]
    [InlineData("pending", "Verify the original cancellation")]
    [InlineData("blocked", "Reconcile the original order")]
    public void Cancellation_recovery_never_suggests_receiving_payment(string status, string expected)
    {
        var hint = CashierWorkflow.Resolve(new(true, PaymentPending: true, CancellationStatus: status));
        Assert.Equal(expected, hint.Title);
        Assert.Equal(CashierDestination.Payment, hint.Destination);
        Assert.DoesNotContain("confirming receipt", hint.Description);
    }

    [Theory]
    [InlineData(false, false, false, CashierDestination.ShiftCash)]
    [InlineData(true, true, false, CashierDestination.ShiftCash)]
    [InlineData(true, false, false, CashierDestination.Checkout)]
    [InlineData(true, true, true, CashierDestination.Checkout)]
    public void Shift_and_cash_guidance_follow_current_prerequisites(bool shift, bool required, bool cash, CashierDestination expected)
        => Assert.Equal(expected, CashierWorkflow.Resolve(new(true, shift, cash, required)).Destination);

    [Fact]
    public void Saved_cash_review_and_refund_have_specific_destinations()
    {
        Assert.Equal(CashierDestination.CashReview, CashierWorkflow.Resolve(new(true, ReviewPending: true)).Destination);
        Assert.Equal(CashierDestination.Receipts, CashierWorkflow.Resolve(new(true, RefundPending: true)).Destination);
    }

    [Fact]
    public void Movements_offer_review_navigation_without_automatic_replay()
    {
        var hint = CashierWorkflow.Resolve(new(true, true, MovementsPending: true));
        Assert.Equal(CashierDestination.Terminal, hint.Destination);
        Assert.Contains("does not replay", hint.Description);
    }

    [Fact]
    public void Original_unpaid_server_total_wins_over_empty_qr_cart_estimate()
    {
        var amount = CashierWorkflow.Amount("THB 0.00", true, 123.45m, "THB");
        Assert.Equal("Unpaid order · server total", amount.Label);
        Assert.Equal(CashierPresentation.Money(123.45m, "THB"), amount.Value);
    }

    [Fact]
    public void Pending_checkout_does_not_show_zero_as_original_order_total()
    {
        var amount = CashierWorkflow.Amount("THB 0.00", true, null, "THB");
        Assert.Equal("Verify original order", amount.Value);
    }

    [Fact]
    public void New_cart_is_explicitly_an_estimate_and_never_paid()
    {
        var amount = CashierWorkflow.Amount("THB 125.00", false, null, "THB");
        Assert.Contains("estimate", amount.Label);
        Assert.Equal("THB 125.00", amount.Value);
    }

    [Theory]
    [InlineData(false, false, false, false, "Sign in")]
    [InlineData(true, false, false, false, "Open a shift")]
    [InlineData(true, true, false, true, "Please wait")]
    [InlineData(true, true, true, false, "Clear filters")]
    [InlineData(true, true, false, false, "Refresh menu")]
    public void Empty_menu_distinguishes_sign_in_shift_busy_filter_and_retry(bool signedIn, bool shift, bool menu, bool busy, string expected)
        => Assert.Contains(expected, CashierWorkflow.MenuEmptyMessage(signedIn, shift, menu, busy));
}
