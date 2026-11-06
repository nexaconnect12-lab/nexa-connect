using System.Windows;
using System.Windows.Controls;
using NexaConnect.POS;

internal static partial class Program
{
    private static void VerifyReceiptViewport(MainWindow window)
    {
        var density = (ComboBox)window.FindName("DensityPicker");
        var preview = (FlowDocumentScrollViewer)window.FindName("ReceiptPreview");
        var refund = (ScrollViewer)window.FindName("RefundFormScroll");
        var viewport = (ScrollViewer)window.FindName("InputViewport");
        var dock = (FrameworkElement)window.FindName("WorkflowDock");
        var originalDocument = preview.Document;
        double originalWidth = window.Width, originalHeight = window.Height;
        int originalDensity = density.SelectedIndex;
        bool refundOverflowVerified = false;
        try
        {
            foreach (int mode in new[] { 0, 1 })
            foreach (var size in new[] { new Size(1366, 768), new Size(1093, 614), new Size(960, 520) })
            {
                density.SelectedIndex = mode; window.Width = size.Width; window.Height = size.Height;
                ((TabItem)window.FindName("ReceiptsTab")).IsSelected = true;
                ((TabItem)window.FindName("ProviderRefundSection")).IsSelected = true;
                preview.Document = ReceiptDocumentFactory.Create(new("Synthetic scroll fixture",
                    string.Join('\n', Enumerable.Range(0, 150).Select(i => $"Synthetic receipt line {i}"))), ReceiptPaper.Roll80);
                Pump(window);
                Require(refund.ActualHeight > 70, "refund form retains a bounded usable viewport");
                if (refund.ScrollableHeight > 0)
                {
                    Require(refund.ComputedVerticalScrollBarVisibility == Visibility.Visible,
                        "overflowing provider refund form exposes a vertical scrollbar");
                    refundOverflowVerified = true;
                }
                refund.ScrollToBottom(); refund.BringIntoView(); Pump(window);
                var button = (Button)window.FindName("LoadRefundButton");
                var buttonBounds = button.TransformToAncestor(refund).TransformBounds(new Rect(button.RenderSize));
                Require(buttonBounds.Top >= -1 && buttonBounds.Bottom <= refund.ActualHeight + 1,
                    "pending-refund action is reachable at the bottom of the refund form");
                ((TabItem)window.FindName("ReceiptPreviewSection")).IsSelected = true; Pump(window);
                Require(preview.ActualHeight >= 100, "receipt preview retains a usable viewport");
                var receiptScroll = Descendants<ScrollViewer>(preview).First(s => s.ScrollableHeight > 0);
                receiptScroll.ScrollToEnd(); preview.BringIntoView(); Pump(window);
                Require(receiptScroll.ComputedVerticalScrollBarVisibility == Visibility.Visible
                    && receiptScroll.VerticalOffset >= receiptScroll.ScrollableHeight - 1,
                    "long receipt can scroll to its final line with a visible scrollbar");
                var previewBounds = preview.TransformToAncestor(window).TransformBounds(new Rect(preview.RenderSize));
                var dockBounds = dock.TransformToAncestor(window).TransformBounds(new Rect(dock.RenderSize));
                var visibleBounds = viewport.TransformToAncestor(window).TransformBounds(new Rect(viewport.RenderSize));
                Require(previewBounds.Bottom <= visibleBounds.Bottom + 1 && previewBounds.Bottom <= dockBounds.Top + 1,
                    "receipt preview bottom can be brought above the workflow panel");
            }
            Require(refundOverflowVerified, "a constrained display exercises the refund scrollbar");
        }
        finally
        {
            preview.Document = originalDocument; window.Width = originalWidth; window.Height = originalHeight;
            density.SelectedIndex = originalDensity; Pump(window);
        }
        Console.WriteLine("PASS: receipt/refund scrolling reaches final content above the workflow dock at 1366x768, scaled and minimum sizes in both densities.");
    }
}
