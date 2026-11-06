using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NexaConnect.POS;

internal static partial class Program
{
    private static void VerifyTableQr(string root)
    {
        var folder = Directory.CreateDirectory(Path.Combine(root, "table-qr")).FullName;
        var now = DateTimeOffset.UtcNow;
        var branch = Guid.NewGuid();
        string path = Path.Combine(folder, "A1" + PosTableQrFiles.Extension);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(384, 384, 96, 96, PixelFormats.Gray8, null,
            new byte[384 * 384], 384)));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        string png = Convert.ToBase64String(stream.ToArray());
        Write(branch, now.AddDays(1));
        var files = new PosTableQrFiles(folder, branch);
        var choice = files.List(now).Single();
        var image = files.Load(choice, now);
        Require(choice.TableCode == "A1" && image.PixelWidth == 384 && image.IsFrozen, "current branch export loads bounded PNG");

        var display = new CustomerDisplayWindow();
        try
        {
            var panel = (FrameworkElement)display.FindName("TableQrPanel");
            var code = (Image)display.FindName("TableQrImage");
            display.TableQr(image, choice.TableCode);
            Require(panel.Visibility == Visibility.Visible && code.Source == image
                && ((TextBlock)display.FindName("TableQrCaption")).Text == "Table A1",
                "public screen shows only the image and table label");
            Require(Grid.GetColumn(panel) == 2 && Grid.GetColumn((FrameworkElement)display.FindName("SaverView")) == 0,
                "table QR and ads occupy separate halves");
            display.TableQr(null, null);
            Require(panel.Visibility == Visibility.Collapsed && code.Source is null, "hiding removes the bitmap");
        }
        finally { display.Close(); }

        Write(Guid.NewGuid(), now.AddDays(1));
        Require(files.List(now).Count == 0, "another branch cannot display this file");
        Write(branch, now.AddSeconds(-1));
        Require(files.List(now).Count == 0, "expired file is not listed");
        Write(branch, now.AddDays(2));
        try { files.Load(choice, now); throw new InvalidOperationException("changed file accepted without reload"); }
        catch (InvalidDataException) { }
        Console.WriteLine("PASS: table QR file scope and expiry, bounded PNG, customer-screen layout and hide.");

        void Write(Guid scope, DateTimeOffset expiry) => File.WriteAllText(path, JsonSerializer.Serialize(
            new { version = 1, branchId = scope, tableCode = "A1", expiresAtUtc = expiry, png }));
    }
}
