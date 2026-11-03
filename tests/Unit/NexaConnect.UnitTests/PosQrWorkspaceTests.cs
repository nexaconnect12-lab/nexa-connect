using NexaConnect.POS;

namespace NexaConnect.UnitTests;

public sealed class PosQrWorkspaceTests
{
    private static PosBranchOrder Item(string status = "KitchenAccepted", string channel = "qr") => new(Guid.NewGuid(), "Test QR",
        DateTimeOffset.UtcNow, channel, status, "THB", 80, "A1", null, status == "Paid" ? "paid" : "awaiting_cashier", status != "Paid");
    private static PosBranchOrderPage Page(PosBranchOrder item) => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), [item], null);

    [Theory]
    [InlineData(false, "open")] [InlineData(true, "paid")]
    public void Views_use_existing_explicit_server_filters(bool history, string filter) => Assert.Equal(filter, PosQrWorkspacePolicy.OrderFilter(history));

    [Theory]
    [InlineData(false, "Paid", "qr")] [InlineData(true, "KitchenAccepted", "qr")]
    [InlineData(false, "KitchenAccepted", "pos")] [InlineData(true, "Paid", "pos")]
    public void Unexpected_source_or_paid_state_cannot_populate_the_wrong_view(bool history, string status, string channel) =>
        Assert.Throws<InvalidDataException>(() => PosQrWorkspacePolicy.ValidatePage(Page(Item(status, channel)), history));

    [Fact]
    public void History_cannot_collect_and_receipt_access_remains_explicit_signed_in_and_idle()
    {
        var paid = Item("Paid") with { CanCollectQr = false }; var state = new PosOrderListState();
        PosQrWorkspacePolicy.ValidatePage(Page(paid), true); state.Apply(state.Generation, Page(paid));
        Assert.False(state.CanCollect(paid.OrderId, true, true, false, false, false, true, "THB"));
        Assert.True(PosQrWorkspacePolicy.CanViewReceipt(paid, true, true, false));
        Assert.False(PosQrWorkspacePolicy.CanViewReceipt(paid, false, true, false));
        Assert.False(PosQrWorkspacePolicy.CanViewReceipt(paid, true, false, false));
        Assert.False(PosQrWorkspacePolicy.CanViewReceipt(paid, true, true, true));
        Assert.False(PosQrWorkspacePolicy.CanViewReceipt(Item(), true, true, false));
        Assert.False(PosQrWorkspacePolicy.CanViewReceipt(null, true, true, false));
    }

    [Fact]
    public void Switching_views_discards_selection_page_cursor_and_late_results_without_changing_order_identity()
    {
        var original = Item(); var state = new PosOrderListState(); int generation = state.Generation;
        var pending = Page(original) with { NextCursor = "next" }; state.Apply(generation, pending);
        state.Clear(); Assert.False(state.Apply(generation, pending)); Assert.Null(state.NextCursor); Assert.Empty(state.Items);
        var paid = original with { Status = "Paid", CollectionStatus = "paid", CanCollectQr = false };
        state.Apply(state.Generation, Page(paid)); Assert.Equal(original.OrderId, Assert.Single(state.Items).OrderId);
    }
}
