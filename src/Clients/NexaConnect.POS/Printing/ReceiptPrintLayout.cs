using System.IO;

namespace NexaConnect.POS;

public sealed record ReceiptPrintLayout(double Width, double Height, double Left, double Top, double Right, double Bottom)
{
    public const double Millimetre = 96d / 25.4;
    public double ContentWidth => Width - Left - Right;
    public static ReceiptPrintLayout Preview(ReceiptPaper paper) => ForPage(paper,
        paper == ReceiptPaper.Roll80 ? 80 * Millimetre : 210 * Millimetre,
        paper == ReceiptPaper.Roll80 ? 200 * Millimetre : 297 * Millimetre, 0, 0,
        paper == ReceiptPaper.Roll80 ? 80 * Millimetre : 210 * Millimetre,
        paper == ReceiptPaper.Roll80 ? 200 * Millimetre : 297 * Millimetre);
    public static ReceiptPrintLayout ForPage(ReceiptPaper paper, double width, double height, double x, double y, double printableWidth, double printableHeight)
    {
        if (!Enum.IsDefined(paper) || new[] { width, height, x, y, printableWidth, printableHeight }.Any(v => !double.IsFinite(v))
            || width <= 0 || height <= 0 || x < 0 || y < 0 || printableWidth <= 0 || printableHeight <= 0
            || x + printableWidth > width + 1 || y + printableHeight > height + 1) throw new InvalidDataException("Printer page geometry is unavailable.");
        double margin = paper == ReceiptPaper.Roll80 ? 4 * Millimetre : 24;
        double left = Math.Max(margin, x), top = Math.Max(margin, y);
        double rightEdge = Math.Min(width - margin, x + printableWidth);
        if (paper == ReceiptPaper.Roll80)
        {
            // Receipt drivers may expose a 72 mm form for nominal 80 mm stock.
            // Fit the actual reported page; never invent bounds or enlarge its content.
            if (width + 1 < 72 * Millimetre) throw new InvalidDataException("Select a receipt form at least 72 mm wide in printer preferences.");
            rightEdge = Math.Min(rightEdge, 76 * Millimetre);
        }
        var result = new ReceiptPrintLayout(width, height, left, top, width - rightEdge, Math.Max(margin, height - y - printableHeight));
        // Avoid rejecting an exact 64 mm result through floating-point subtraction.
        const double arithmeticTolerance = 0.0000001;
        if (result.ContentWidth + arithmeticTolerance < 64 * Millimetre || height - result.Top - result.Bottom + arithmeticTolerance < 20 * Millimetre)
            throw new InvalidDataException("Printer printable area is too small for this receipt layout.");
        return result;
    }
}
