using NexaConnect.POS;

namespace NexaConnect.UnitTests;

public sealed class CashierPresentationTests
{
    [Theory]
    [InlineData(true, true, false, false, true)]
    [InlineData(false, true, false, false, false)]
    [InlineData(true, false, false, false, false)]
    [InlineData(true, true, true, false, false)]
    [InlineData(true, true, false, true, false)]
    public void Cart_edits_require_an_idle_authenticated_shift_without_pending_payment(
        bool signedIn, bool shiftOpen, bool busy, bool pending, bool expected) =>
        Assert.Equal(expected, CashierPresentation.CanEditCart(signedIn, shiftOpen, busy, pending));

    [Theory]
    [InlineData(true, true, false, false, 1, true)]
    [InlineData(true, true, false, true, 0, true)]
    [InlineData(true, true, false, false, 0, false)]
    [InlineData(true, true, true, false, 1, false)]
    [InlineData(true, false, false, false, 1, false)]
    [InlineData(false, true, false, false, 1, false)]
    public void Order_action_exposes_valid_cart_or_recovery_and_keeps_payment_as_a_blocker(
        bool signedIn, bool shiftOpen, bool paymentPending, bool checkoutPending,
        int cartLineCount, bool expected) =>
        Assert.Equal(expected, CashierPresentation.CanAttemptOrder(
            signedIn, shiftOpen, paymentPending, checkoutPending, cartLineCount));

    [Theory]
    [InlineData("  RICE ", "All stations", true)]
    [InlineData("rice", "Hot kitchen", true)]
    [InlineData("rice", "Bar", false)]
    [InlineData("tea", "Hot kitchen", false)]
    [InlineData("", "All stations", true)]
    public void Search_and_station_filters_are_combined(string search, string station, bool expected) =>
        Assert.Equal(expected, CashierPresentation.MatchesMenu("Fried rice", "Hot kitchen", search, station));

    [Fact]
    public void Total_uses_order_currency_instead_of_machine_currency() =>
        Assert.StartsWith("THB ", CashierPresentation.Money(120.50m, "thb"));

    [Theory]
    [InlineData("THB", "THB", true)]
    [InlineData("thb", "THB", true)]
    [InlineData("SGD", "THB", false)]
    public void Cart_rejects_mixed_currency_prices(string item, string order, bool expected) =>
        Assert.Equal(expected, CashierPresentation.MatchesCurrency(item, order));

    [Theory]
    [InlineData(false, true, false, false, false, false, "Sign in")]
    [InlineData(true, true, false, false, true, false, "Payment")]
    [InlineData(true, true, false, true, false, false, "Checkout")]
    [InlineData(true, true, true, false, false, true, "Terminal & sync")]
    [InlineData(true, true, true, false, false, false, "Close cash session")]
    [InlineData(true, true, false, false, false, false, "Close shift")]
    [InlineData(true, false, false, false, false, false, "Open a shift")]
    public void Session_guidance_identifies_the_next_operator_action(
        bool signedIn, bool shiftOpen, bool cashOpen, bool checkoutPending,
        bool paymentPending, bool movementsPending, string expected) =>
        Assert.Contains(expected, CashierPresentation.SessionGuidance(
            signedIn, shiftOpen, cashOpen, checkoutPending, paymentPending, movementsPending));
}
