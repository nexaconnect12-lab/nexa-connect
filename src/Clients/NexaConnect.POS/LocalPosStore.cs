using System.Globalization;
using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace NexaConnect.POS;

public sealed record LocalShiftState(Guid ShiftId, string ShiftNumber, DateTimeOffset OpenedAtUtc);
public sealed record LocalCashSessionState(Guid CashSessionId, Guid ShiftId, DateTimeOffset OpenedAtUtc);
public sealed record LocalPendingSettlementState(Guid OrderId, decimal Amount, string Currency,
    Guid IdempotencyKey, string? Method = null, bool ReceiptConfirmed = false, string? BankReference = null,
    bool OutcomeUncertain = false);

public sealed class LocalPosStore
{
    private readonly LocalPosDatabase database;

    public LocalPosStore(LocalPosScope scope, string? storageDirectory = null)
        : this(storageDirectory, null, scope)
    {
    }

    internal LocalPosStore(string? storageDirectory = null, ILocalPayloadProtector? payloadProtector = null,
        LocalPosScope? scope = null)
    {
        database = new LocalPosDatabase(storageDirectory, payloadProtector, scope);
    }

    public LocalShiftState? LoadActiveShift() => Read<LocalShiftState>("active-shift",
        "The saved shift cannot be read. Preserve the local POS database and reconcile the shift.");

    public void SaveActiveShift(LocalShiftState state)
    {
        if (state.ShiftId == Guid.Empty || string.IsNullOrWhiteSpace(state.ShiftNumber))
            throw new InvalidDataException("The active shift state is invalid.");
        Write("active-shift", state);
    }

    public void ClearActiveShift() => Delete("active-shift");

    public LocalCashSessionState? LoadCashSession() => Read<LocalCashSessionState>("cash-session",
        "The saved cash session cannot be read. Preserve the local POS database and reconcile the drawer.");

    public void SaveCashSession(LocalCashSessionState state)
    {
        if (state.CashSessionId == Guid.Empty || state.ShiftId == Guid.Empty)
            throw new InvalidDataException("The cash-session state is invalid.");
        Write("cash-session", state);
    }

    public void ClearCashSession() => Delete("cash-session");

    public LocalPendingSettlementState? LoadPendingSettlement() => Read<LocalPendingSettlementState>(
        "pending-settlement",
        "The pending settlement cannot be read. Preserve the local POS database for reconciliation; do not collect payment again.");

    public void SavePendingSettlement(LocalPendingSettlementState state)
    {
        if (state.OrderId == Guid.Empty || state.IdempotencyKey == Guid.Empty || state.Amount <= 0
            || !string.Equals(state.Currency, "THB", StringComparison.Ordinal))
            throw new InvalidDataException("The pending settlement state is invalid.");
        Write("pending-settlement", state);
    }

    public void ClearPendingSettlement() => Delete("pending-settlement");

    public PendingCheckout? LoadPendingCheckout() => Read<PendingCheckout>("pending-checkout",
        "Pending checkout recovery cannot be read. Preserve the local POS database and reconcile; do not submit another order.");

    public void SavePendingCheckout(PendingCheckout checkout)
    {
        if (checkout.OrderId == Guid.Empty || checkout.SettlementKey == Guid.Empty)
            throw new InvalidDataException("The pending checkout state is invalid.");
        Write("pending-checkout", checkout);
    }

    public void ClearPendingCheckout() => Delete("pending-checkout");

    public void ClearCheckoutAndSettlement()
    {
        using SqliteConnection connection = database.OpenConnection();
        using SqliteTransaction transaction = connection.BeginTransaction();
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM local_state WHERE state_key IN ('pending-checkout','pending-settlement');";
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    private T? Read<T>(string key, string failureMessage)
    {
        try
        {
            using SqliteConnection connection = database.OpenConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT protected_payload FROM local_state WHERE state_key=$key;";
            command.Parameters.AddWithValue("$key", key);
            object? value = command.ExecuteScalar();
            if (value is null) return default;
            byte[] plaintext = database.Unprotect((byte[])value);
            return JsonSerializer.Deserialize<T>(plaintext)
                ?? throw new InvalidDataException(failureMessage);
        }
        catch (Exception exception) when (exception is not InvalidDataException)
        {
            throw new InvalidDataException(failureMessage, exception);
        }
    }

    private void Write<T>(string key, T state)
    {
        byte[] protectedPayload = database.Protect(JsonSerializer.SerializeToUtf8Bytes(state));
        using SqliteConnection connection = database.OpenConnection();
        using SqliteTransaction transaction = connection.BeginTransaction();
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO local_state(state_key, protected_payload, updated_at_utc)
            VALUES($key, $payload, $updated)
            ON CONFLICT(state_key) DO UPDATE SET
                protected_payload=excluded.protected_payload,
                updated_at_utc=excluded.updated_at_utc;
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$payload", protectedPayload);
        command.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    private void Delete(string key)
    {
        using SqliteConnection connection = database.OpenConnection();
        using SqliteTransaction transaction = connection.BeginTransaction();
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM local_state WHERE state_key=$key;";
        command.Parameters.AddWithValue("$key", key);
        command.ExecuteNonQuery();
        transaction.Commit();
    }
}
