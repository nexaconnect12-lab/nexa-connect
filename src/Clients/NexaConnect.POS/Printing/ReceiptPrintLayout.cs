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
            if (width + 1 < 80 * Millimetre) throw new InvalidDataException("Select an 80 mm or wider paper size in printer preferences.");
            rightEdge = Math.Min(rightEdge, 76 * Millimetre);
        }
        var result = new ReceiptPrintLayout(width, height, left, top, width - rightEdge, Math.Max(margin, height - y - printableHeight));
        if (result.ContentWidth < 64 * Millimetre || height - result.Top - result.Bottom < 20 * Millimetre)
            throw new InvalidDataException("Printer printable area is too small for this receipt layout.");
        return result;
    }
}
