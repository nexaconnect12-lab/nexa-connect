using System.IO;

namespace NexaConnect.POS;

public static class PosQrWorkspacePolicy
{
    public static string OrderFilter(bool history) => history ? "paid" : "open";
    public static void ValidatePage(PosBranchOrderPage page, bool history)
    {
        // The shared adapter validates tenant/cursor/row bounds; this surface must remain QR-only.
        if (page.Items.Any(i => i.Channel != "qr" || (history ? i.Status != "Paid" : i.Status == "Paid")))
            throw new InvalidDataException("Unexpected QR workspace page.");
    }
    public static bool CanViewReceipt(PosBranchOrder? item, bool history, bool signedIn, bool busy) =>
        history && signedIn && !busy && item is { Channel: "qr", Status: "Paid" };
}
