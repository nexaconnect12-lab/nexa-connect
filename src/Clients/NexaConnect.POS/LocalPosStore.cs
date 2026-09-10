using System.IO;
using System.Text.Json;

namespace NexaConnect.POS;

public sealed record LocalShiftState(Guid ShiftId, string ShiftNumber, DateTimeOffset OpenedAtUtc);
public sealed record LocalCashSessionState(Guid CashSessionId, Guid ShiftId, DateTimeOffset OpenedAtUtc);
public sealed record LocalPendingSettlementState(Guid OrderId, decimal Amount, string Currency,
    Guid IdempotencyKey, string? Method = null, bool ReceiptConfirmed = false, string? BankReference = null,
    bool OutcomeUncertain = false);

public sealed class LocalPosStore
{
    private readonly string _path;

    public LocalPosStore(string? storageDirectory = null)
    {
        string directory = storageDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NexaConnect",
            "POS");
        _path = Path.Combine(directory, "state.json");
    }

    public LocalShiftState? LoadActiveShift()
    {
        try
        {
            return File.Exists(_path)
                ? JsonSerializer.Deserialize<LocalShiftState>(File.ReadAllText(_path))
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public void SaveActiveShift(LocalShiftState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(state));
    }

    public void ClearActiveShift()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }

    private string CashPath => Path.Combine(Path.GetDirectoryName(_path)!, "cash-session.json");

    public LocalCashSessionState? LoadCashSession()
    {
        try { return File.Exists(CashPath) ? JsonSerializer.Deserialize<LocalCashSessionState>(File.ReadAllText(CashPath)) : null; }
        catch (JsonException) { return null; }
    }

    public void SaveCashSession(LocalCashSessionState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(CashPath)!);
        File.WriteAllText(CashPath, JsonSerializer.Serialize(state));
    }

    public void ClearCashSession() { if (File.Exists(CashPath)) File.Delete(CashPath); }

    private string SettlementPath => Path.Combine(Path.GetDirectoryName(_path)!, "pending-settlement.bin");

    public LocalPendingSettlementState? LoadPendingSettlement()
    {
        if (!File.Exists(SettlementPath)) return null;
        try
        {
            byte[] plaintext = WindowsDataProtection.Unprotect(File.ReadAllBytes(SettlementPath));
            return JsonSerializer.Deserialize<LocalPendingSettlementState>(plaintext)
                ?? throw new InvalidDataException("The pending settlement recovery file is empty.");
        }
        catch (Exception exception) when (exception is not InvalidDataException)
        {
            throw new InvalidDataException(
                "The pending settlement recovery file cannot be read. Preserve it for reconciliation; do not collect payment again.",
                exception);
        }
    }

    public void SavePendingSettlement(LocalPendingSettlementState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettlementPath)!);
        string temporaryPath = SettlementPath + ".tmp";
        byte[] plaintext = JsonSerializer.SerializeToUtf8Bytes(state);
        File.WriteAllBytes(temporaryPath, WindowsDataProtection.Protect(plaintext));
        File.Move(temporaryPath, SettlementPath, true);
    }

    public void ClearPendingSettlement() { if (File.Exists(SettlementPath)) File.Delete(SettlementPath); }

    private string CheckoutPath => Path.Combine(Path.GetDirectoryName(_path)!, "pending-checkout.bin");

    public PendingCheckout? LoadPendingCheckout()
    {
        if (!File.Exists(CheckoutPath)) return null;
        try
        {
            return JsonSerializer.Deserialize<PendingCheckout>(WindowsDataProtection.Unprotect(File.ReadAllBytes(CheckoutPath)))
                ?? throw new InvalidDataException("Empty pending checkout.");
        }
        catch (Exception exception)
        {
            throw new InvalidDataException("Pending checkout recovery cannot be read. Preserve the file and reconcile; do not submit another order.", exception);
        }
    }

    public void SavePendingCheckout(PendingCheckout checkout)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(CheckoutPath)!);
        string temporary = CheckoutPath + ".tmp";
        File.WriteAllBytes(temporary, WindowsDataProtection.Protect(JsonSerializer.SerializeToUtf8Bytes(checkout)));
        File.Move(temporary, CheckoutPath, true);
    }

    public void ClearPendingCheckout() { if (File.Exists(CheckoutPath)) File.Delete(CheckoutPath); }
}
