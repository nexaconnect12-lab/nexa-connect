using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace NexaConnect.POS;

public static class ReceiptDocumentFactory
{
    public static FlowDocument Create(ReceiptPrintContent content, ReceiptPaper paper, ReceiptPrintLayout? page = null)
    {
        page ??= ReceiptPrintLayout.Preview(paper);
        var document = new FlowDocument { FontFamily = new FontFamily(paper == ReceiptPaper.Roll80 ? "Segoe UI" : "Consolas"),
            FontSize = 12, Foreground = Brushes.Black, Background = Brushes.White, PageWidth = page.Width, PageHeight = page.Height,
            PagePadding = new Thickness(page.Left, page.Top, page.Right, page.Bottom),
            ColumnWidth = double.PositiveInfinity, IsHyphenationEnabled = false, TextAlignment = TextAlignment.Left };
        // Literal text only; no XAML/markup, truncation or recalculation of server amounts.
        foreach (string line in content.Text.Replace("\r\n", "\n").Split('\n'))
        {
            string safe = new(line.Select(c => char.IsControl(c) ? ' ' : c).ToArray());
            bool emphasis = safe.StartsWith("TOTAL ", StringComparison.Ordinal) || safe.StartsWith("SAMPLE TOTAL ", StringComparison.Ordinal)
                || safe.Contains("NOT A SALE", StringComparison.Ordinal);
            document.Blocks.Add(new Paragraph(new Run(safe)) { Margin = new Thickness(0),
                FontWeight = emphasis ? FontWeights.Bold : FontWeights.Normal, FontSize = emphasis ? 14 : 12 });
        }
        return document;
    }
}
