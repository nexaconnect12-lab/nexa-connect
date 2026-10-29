namespace NexaConnect.Services.Catalog.Domain.Barcodes;

public static class ProductBarcodeValue
{
    public static string Parse(string? value)
    {
        if (value is null || value.Length is < 1 or > 64 || !value.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-'))
            throw new ArgumentException("Use 1–64 ASCII letters, digits, dots, underscores or hyphens. Spaces are not supported.");
        return value; // Case and leading zeroes are significant.
    }
}
public sealed record BarcodeWriteState(long Version, string? Barcode, Guid? RegisteredProduct);
public sealed record BarcodeChange(long Version, string? Barcode, bool Changed);
public sealed class BarcodeConflictException : Exception;
public static class BarcodeAssignment
{
    public static BarcodeChange Change(BarcodeWriteState current, Guid product, string? barcode, long expectedVersion)
    {
        if (product == Guid.Empty || expectedVersion < 0) throw new ArgumentException("A product and non-negative version are required.");
        if (barcode is not null) ProductBarcodeValue.Parse(barcode);
        if (current.Version != expectedVersion || barcode is not null && current.RegisteredProduct is { } owner && owner != product)
            throw new BarcodeConflictException();
        return barcode == current.Barcode ? new(current.Version, barcode, false)
            : new(checked(current.Version + 1), barcode, true);
    }
}
