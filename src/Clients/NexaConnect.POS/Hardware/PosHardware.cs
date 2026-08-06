namespace NexaConnect.POS.Hardware;

public interface IReceiptPrinter
{
    Task PrintAsync(ReadOnlyMemory<byte> receipt, CancellationToken cancellationToken = default);
}

public interface IBarcodeScanner
{
    event EventHandler<string>? BarcodeScanned;
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}

public interface ICashDrawer
{
    Task OpenAsync(CancellationToken cancellationToken = default);
}

public interface IPosHardware
{
    IReceiptPrinter ReceiptPrinter { get; }
    IBarcodeScanner BarcodeScanner { get; }
    ICashDrawer CashDrawer { get; }
}

public sealed class UnconfiguredPosHardware : IPosHardware
{
    public IReceiptPrinter ReceiptPrinter { get; } = new UnconfiguredReceiptPrinter();
    public IBarcodeScanner BarcodeScanner { get; } = new UnconfiguredBarcodeScanner();
    public ICashDrawer CashDrawer { get; } = new UnconfiguredCashDrawer();

    private sealed class UnconfiguredReceiptPrinter : IReceiptPrinter
    {
        public Task PrintAsync(ReadOnlyMemory<byte> receipt, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("No POS receipt printer is configured.");
    }

    private sealed class UnconfiguredBarcodeScanner : IBarcodeScanner
    {
        public event EventHandler<string>? BarcodeScanned
        {
            add { }
            remove { }
        }
        public Task StartAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("No POS barcode scanner is configured.");
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class UnconfiguredCashDrawer : ICashDrawer
    {
        public Task OpenAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("No POS cash drawer is configured.");
    }
}
