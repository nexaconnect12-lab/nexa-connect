using System.Globalization;
using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace NexaConnect.POS;

public sealed record LocalReceiptReference(Guid OrderId);
public sealed record LocalPendingRefundState(Guid PaymentIntentId, Guid OperationId, decimal Amount,
    string Currency, string ReasonCode, Guid? RefundId = null);
public sealed record LocalRefundReference(Guid PaymentIntentId, Guid OperationId);
public sealed record LocalShiftState(Guid ShiftId, string ShiftNumber, DateTimeOffset OpenedAtUtc);
public sealed record LocalCashSessionState(Guid CashSessionId, Guid ShiftId, DateTimeOffset OpenedAtUtc);
public sealed record LocalPendingSettlementState(Guid OrderId, decimal Amount, string Currency,
    Guid IdempotencyKey, string? Method = null, bool ReceiptConfirmed = false, string? BankReference = null,
    bool OutcomeUncertain = false, PosOrderPricing? Pricing = null, string? CancellationStatus = null,
    string? CancellationReason = null);
public sealed record LocalPendingCashReviewState(
    Guid IdempotencyKey,
    Guid CashSessionId,
    Guid OrganizationId,
    Guid RestaurantId,
    Guid BranchId,
    Guid StoreId,
    Guid TerminalId,
    string Decision,
    string Reason,
    long ExpectedSessionVersion,
    long ExpectedReviewVersion)
{
    public static LocalPendingCashReviewState Create(PosClientConfiguration configuration,
        Guid cashSessionId, string decision, string reason, long expectedSessionVersion,
        long expectedReviewVersion) => new(Guid.NewGuid(), cashSessionId,
            configuration.OrganizationId, configuration.RestaurantId, configuration.BranchId,
            configuration.StoreId, configuration.TerminalId, decision, reason,
            expectedSessionVersion, expectedReviewVersion);

    public void Validate(PosClientConfiguration configuration)
    {
        if (IdempotencyKey == Guid.Empty || CashSessionId == Guid.Empty ||
            OrganizationId == Guid.Empty || RestaurantId == Guid.Empty || BranchId == Guid.Empty ||
            StoreId == Guid.Empty || TerminalId == Guid.Empty ||
            Decision is not ("approve" or "investigate") || string.IsNullOrWhiteSpace(Reason) ||
            Reason != Reason.Trim() || Reason.Length > 200 || ExpectedSessionVersion <= 0 ||
            ExpectedReviewVersion < 0)
            throw new InvalidDataException("The pending cash-review state is invalid.");
        if (OrganizationId != configuration.OrganizationId || RestaurantId != configuration.RestaurantId ||
            BranchId != configuration.BranchId || StoreId != configuration.StoreId ||
            TerminalId != configuration.TerminalId)
            throw new InvalidDataException(
                "The pending cash review belongs to a different organization, restaurant, branch, store, or terminal.");
    }

    public bool SameRequest(LocalPendingCashReviewState other) =>
        CashSessionId == other.CashSessionId && Decision == other.Decision && Reason == other.Reason &&
        ExpectedSessionVersion == other.ExpectedSessionVersion &&
        ExpectedReviewVersion == other.ExpectedReviewVersion;
}

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

    public LocalReceiptReference? LoadLastReceipt() => Read<LocalReceiptReference>("last-receipt", "The last receipt reference cannot be read.");
    public void SaveLastReceipt(Guid orderId)
    {
        if (orderId == Guid.Empty) throw new ArgumentException("Order identity is required.");
        Write("last-receipt", new LocalReceiptReference(orderId));
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
            || !string.Equals(state.Currency, "THB", StringComparison.Ordinal)
            || state.CancellationStatus is not (null or "pending" or "blocked")
            || (state.CancellationStatus is null) != (state.CancellationReason is null)
            || (state.CancellationReason is not null && (state.CancellationReason.Length is < 1 or > 200
                || state.CancellationReason != state.CancellationReason.Trim()
                || state.CancellationReason.Any(char.IsControl))))
            throw new InvalidDataException("The pending settlement state is invalid.");
        Write("pending-settlement", state);
    }

    public void ClearPendingSettlement() => Delete("pending-settlement");

    public LocalPendingRefundState? LoadPendingRefund() => Read<LocalPendingRefundState>("pending-refund",
        "The pending refund cannot be read. Preserve the local POS database and verify the provider result before another refund.");
    public void SavePendingRefund(LocalPendingRefundState state)
    {
        if (state.PaymentIntentId == Guid.Empty || state.OperationId == Guid.Empty || state.Amount <= 0
            || state.Currency != "THB" || state.ReasonCode is not ("customer_request" or "duplicate_charge"
                or "item_unavailable" or "service_issue" or "other"))
            throw new InvalidDataException("The pending refund state is invalid.");
        Write("pending-refund", state);
    }
    public void ClearPendingRefund() => Delete("pending-refund");
    public LocalRefundReference? LoadLastRefund() => Read<LocalRefundReference>("last-refund",
        "The last refund reference cannot be read.");
    public void SaveLastRefund(Guid paymentIntentId, Guid operationId)
    {
        if (paymentIntentId == Guid.Empty || operationId == Guid.Empty) throw new ArgumentException("Refund identity is required.");
        Write("last-refund", new LocalRefundReference(paymentIntentId, operationId));
    }

    public PendingCheckout? LoadPendingCheckout() => Read<PendingCheckout>("pending-checkout",
        "Pending checkout recovery cannot be read. Preserve the local POS database and reconcile; do not submit another order.");

    public void SavePendingCheckout(PendingCheckout checkout)
    {
        if (checkout.OrderId == Guid.Empty || checkout.SettlementKey == Guid.Empty)
            throw new InvalidDataException("The pending checkout state is invalid.");
        Write("pending-checkout", checkout);
    }

    public void ClearPendingCheckout() => Delete("pending-checkout");

    public LocalPendingCashReviewState? LoadPendingCashReview(PosClientConfiguration configuration)
    {
        LocalPendingCashReviewState? state = Read<LocalPendingCashReviewState>("pending-cash-review",
            "Pending cash-review recovery cannot be read. Preserve the local POS database and reconcile the supervisor decision.");
        state?.Validate(configuration);
        return state;
    }

    public void SavePendingCashReview(LocalPendingCashReviewState state, PosClientConfiguration configuration)
    {
        state.Validate(configuration);
        Write("pending-cash-review", state);
    }

    public void ClearPendingCashReview() => Delete("pending-cash-review");

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
