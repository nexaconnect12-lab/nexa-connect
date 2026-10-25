namespace NexaConnect.POS;

// Session-local display data only; selecting a row never grants financial authority.
public sealed class PosOrderListState
{
    public int Generation { get; private set; }
    public IReadOnlyList<PosBranchOrder> Items { get; private set; } = [];
    public string? NextCursor { get; private set; }

    public int Clear()
    {
        Items = []; NextCursor = null;
        return ++Generation;
    }

    public bool Apply(int generation, PosBranchOrderPage page)
    {
        if (generation != Generation) return false;
        Items = Array.AsReadOnly(page.Items.ToArray()); NextCursor = page.NextCursor;
        return true;
    }

    public bool CanCollect(Guid? selectedOrder, bool signedIn, bool openShift, bool busy,
        bool pendingWork, bool hasCart, bool manualTender, string currency) =>
        signedIn && openShift && !busy && !pendingWork && !hasCart && manualTender
        && Items.Any(i => i.OrderId == selectedOrder && i.CanCollectQr && i.Currency == currency);

    public string CollectionGuidance(Guid? selectedOrder, bool signedIn, bool openShift, bool busy,
        bool pendingWork, bool hasCart, bool manualTender, string currency)
    {
        if (!signedIn) return "Unlock or sign in to view and collect orders.";
        if (busy) return "Wait for the current action to finish.";
        if (pendingWork) return "Verify the original pending checkout or payment before collecting another order.";
        if (!openShift) return "Open a shift before collecting payment.";
        if (hasCart) return "Finish or clear the current cart before loading a QR order.";
        if (!manualTender) return "QR collection requires cash or manual PromptPay configuration.";
        var item = Items.FirstOrDefault(i => i.OrderId == selectedOrder);
        if (item is null) return "Select a QR order to see collection availability.";
        if (item.Status == "Paid" || item.CollectionStatus == "paid") return "Already paid. Do not collect payment again.";
        if (item.Channel != "qr") return "This is a POS order; QR collection applies to accepted QR orders.";
        if (item.CollectionStatus == "collecting") return "Collection has already started. Use the original cashier's pending checkout to continue.";
        if (item.Currency != currency) return "Order currency differs from this terminal. Ask a manager for help.";
        if (!item.CanCollectQr) return "Collection is unavailable. Check preparation status and your branch payment permission.";
        return "Ready to load. The original order and payment authority will be checked again.";
    }
}
