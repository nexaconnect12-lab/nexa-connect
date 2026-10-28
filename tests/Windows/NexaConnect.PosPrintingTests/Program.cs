using System.IO;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NexaConnect.POS;

// Synthetic receipt rendering only. Never authenticates, opens a printer or touches live POS data.
internal static class Program
{
    private static int glyphs;
    [STAThread] private static void Main()
    {
        Guid order = Guid.NewGuid();
        var receipt = new PosReceipt(1, $"R-{order:N}".ToUpperInvariant(), order, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "ORDER-123", DateTimeOffset.UtcNow, "THB", "cash_manual",
            Enumerable.Range(0, 60).Select(i => new PosReceiptLine(Guid.NewGuid(), $"Item {i} — ข้าวผัด / Noodles & tea <plain text> " + new string('W', 120), 123.45m, 2, 246.90m)).ToArray(),
            null, 14814m, 0, 0, 14814m);
        foreach (var paper in new[] { ReceiptPaper.Standard, ReceiptPaper.Roll80 })
        {
            var content = new ReceiptPrintContent("Synthetic sales receipt", receipt.Render());
            var document = ReceiptDocumentFactory.Create(content, paper);
            string rendered = new TextRange(document.ContentStart, document.ContentEnd).Text;
            Require(rendered.Contains(receipt.ReceiptNumber) && rendered.Contains("TOTAL THB 14814.00") && rendered.Contains("Item 59"), "exact receipt content remains");
            Require(rendered.Contains("<plain text>"), "receipt names stay literal, not markup");
            var paginator = ((IDocumentPaginatorSource)document).DocumentPaginator; paginator.ComputePageCount();
            Require(paginator.PageCount > 1, "long receipt paginates");
            for (int page = 0; page < paginator.PageCount; page++)
            {
                using var result = paginator.GetPage(page);
                Require(result != DocumentPage.Missing && result.Size.Width == document.PageWidth, "page width retained");
                VerifyGlyphBounds(result.Visual, result.Visual, new Rect(document.PagePadding.Left, document.PagePadding.Top,
                    document.PageWidth - document.PagePadding.Left - document.PagePadding.Right, document.PageHeight - document.PagePadding.Top - document.PagePadding.Bottom));
                if (page == 0) Save(result, $".runstate/pos-ux5-{paper}-preview.png");
            }
        }
        Require(glyphs > 0, "glyph-bound assertions exercised real rendered text");
        var test = ReceiptDocumentFactory.Create(ReceiptPrintContent.Test, ReceiptPaper.Roll80);
        Require(new TextRange(test.ContentStart, test.ContentEnd).Text.Contains("TEST PRINT — NOT A SALE"), "test print is distinct");
        var compact = receipt with { Lines = [new(Guid.NewGuid(), "Fried rice / ข้าวผัด", 42, 1, 42), new(Guid.NewGuid(), "Iced tea", 35, 1, 35)], SubtotalAmount = 77, TotalAmount = 77 };
        var compactDocument = ReceiptDocumentFactory.Create(new("Synthetic receipt", compact.Render()), ReceiptPaper.Roll80);
        var compactPaginator = ((IDocumentPaginatorSource)compactDocument).DocumentPaginator; compactPaginator.ComputePageCount();
        Require(compactPaginator.PageCount == 1, "ordinary short receipt stays one page");
        using (var page = compactPaginator.GetPage(0)) Save(page, ".runstate/pos-ux5-Roll80-sample.png");
        var pdfLayout = ReceiptPrintLayout.ForPage(ReceiptPaper.Roll80, 793.7, 1122.5, 10, 10, 773.7, 1102.5);
        var pdf = ReceiptDocumentFactory.Create(ReceiptPrintContent.Test, ReceiptPaper.Roll80, pdfLayout);
        Require(Math.Abs(pdf.PageWidth - pdf.PagePadding.Left - pdf.PagePadding.Right - 72 * ReceiptPrintLayout.Millimetre) < 0.01, "wider paper keeps roll content width");
        Console.WriteLine("PASS: Standard/80 mm synthetic receipt pagination, all-page glyph bounds, long names, Thai text, exact totals, literal markup and test labels. No spool job or live POS state accessed.");
    }
    private static void VerifyGlyphBounds(Visual visual, Visual root, Rect content)
    {
        var transform = visual == root ? Transform.Identity : visual.TransformToAncestor(root);
        if (VisualTreeHelper.GetDrawing(visual) is { } drawing) CheckDrawing(drawing, Matrix.Identity, transform, content);
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(visual); i++)
            if (VisualTreeHelper.GetChild(visual, i) is Visual child) VerifyGlyphBounds(child, root, content);
    }
    private static void CheckDrawing(Drawing drawing, Matrix matrix, GeneralTransform visual, Rect content)
    {
        if (drawing is DrawingGroup group)
        {
            var next = group.Transform?.Value ?? Matrix.Identity; next.Append(matrix);
            foreach (var child in group.Children) CheckDrawing(child, next, visual, content);
        }
        else if (drawing is GlyphRunDrawing glyph)
        {
            glyphs++;
            Rect bounds = visual.TransformBounds(new MatrixTransform(matrix).TransformBounds(glyph.Bounds));
            Require(bounds.Left >= content.Left - 2 && bounds.Right <= content.Right + 2 && bounds.Top >= content.Top - 2 && bounds.Bottom <= content.Bottom + 2, "glyph remains in printable receipt area");
        }
    }
    private static void Save(DocumentPage page, string path)
    {
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(page.Size.Width), (int)Math.Ceiling(page.Size.Height), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(page.Visual); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!); using var output = File.Create(path); encoder.Save(output);
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
