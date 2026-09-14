namespace NexaConnect.Services.POS.Application.CashSessions;

using System.Globalization;
using System.Security.Cryptography;
using System.Text;

public sealed record OpenCashSessionCommand(Guid ShiftId, Guid StoreId, string? Currency, decimal OpeningAmount);

public sealed record RecordCashMovementCommand(
    Guid CashSessionId,
    string MovementType,
    decimal Amount,
    string? ReasonCode,
    Guid? ClientOperationId = null,
    Guid? TerminalId = null);

public sealed record CashMovementSummary(
    Guid MovementId,
    string MovementType,
    decimal Amount,
    string? ReasonCode,
    DateTimeOffset OccurredAtUtc);

public sealed record CashSessionSummary(
    Guid CashSessionId,
    Guid ShiftId,
    Guid StoreId,
    Guid TerminalId,
    string Currency,
    decimal OpeningAmount,
    decimal NetMovementAmount,
    decimal ExpectedClosingAmount,
    decimal? ActualClosingAmount,
    decimal? VarianceAmount,
    string Status,
    DateTimeOffset OpenedAtUtc,
    DateTimeOffset? ClosedAtUtc,
    long ConcurrencyVersion,
    IReadOnlyList<CashMovementSummary> Movements);

public interface ICashSessionStore
{
    Task<Guid> OpenAsync(Guid shiftId, Guid storeId, string currency, decimal openingAmount, CancellationToken cancellationToken);
    Task<bool> RecordMovementAsync(Guid cashSessionId, string movementType, decimal amount, string recordedBy, string? reasonCode, Guid? clientOperationId, Guid? terminalId, string payloadHash, CancellationToken cancellationToken);
    Task<CashSessionSummary?> GetSummaryAsync(Guid cashSessionId, string subject, Guid terminalId, CancellationToken cancellationToken);
    Task CloseAsync(Guid cashSessionId, decimal actualClosingAmount, long expectedConcurrencyVersion,
        string subject, Guid terminalId, CancellationToken cancellationToken);
}

public sealed class CashSessionApplicationService(ICashSessionStore store)
{
    private static readonly HashSet<string> MovementTypes =
        ["sale", "refund", "pay_in", "pay_out", "float_adjustment"];

    public async Task<Guid> OpenAsync(
        OpenCashSessionCommand command,
        string subject,
        CancellationToken cancellationToken)
    {
        RequireSubject(subject);
        if (command.ShiftId == Guid.Empty || command.StoreId == Guid.Empty || command.OpeningAmount < 0 ||
            command.Currency is null || command.Currency.Length != 3 || !command.Currency.All(char.IsAsciiLetter))
        {
            throw new CashSessionValidationException("Shift, store, a three-letter currency, and a non-negative opening amount are required.");
        }

        try
        {
            return await store.OpenAsync(
                command.ShiftId,
                command.StoreId,
                command.Currency.ToUpperInvariant(),
                command.OpeningAmount,
                cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            throw new CashSessionConflictException(exception.Message, exception);
        }
    }

    public async Task<bool> RecordMovementAsync(
        RecordCashMovementCommand command,
        string subject,
        CancellationToken cancellationToken)
    {
        RequireSubject(subject);
        if (command.CashSessionId == Guid.Empty || command.Amount <= 0 ||
            !MovementTypes.Contains(command.MovementType))
        {
            throw new CashSessionValidationException("Cash session, movement type, and a positive amount are required.");
        }
        if (command.ClientOperationId is null || command.TerminalId is null)
        {
            throw new CashSessionValidationException("Client operation and terminal identifiers are required for cash movements.");
        }

        try
        {
            return await store.RecordMovementAsync(
                command.CashSessionId,
                command.MovementType,
                command.Amount,
                subject,
                command.ReasonCode,
                command.ClientOperationId,
                command.TerminalId,
                ComputeMovementHash(command),
                cancellationToken);
        }
        catch (DuplicateSyncOperationException exception)
        {
            throw new CashSessionConflictException(exception.Message, exception);
        }
        catch (CashSessionReplayAuthorizationException)
        {
            throw;
        }
        catch (InvalidOperationException exception)
        {
            throw new CashSessionConflictException(exception.Message, exception);
        }
    }

    public async Task<CashSessionSummary> GetSummaryAsync(
        Guid cashSessionId,
        string subject,
        Guid terminalId,
        CancellationToken cancellationToken)
    {
        RequireSubject(subject);
        if (cashSessionId == Guid.Empty || terminalId == Guid.Empty)
            throw new CashSessionValidationException("Cash session and terminal identifiers are required.");

        return await store.GetSummaryAsync(cashSessionId, subject, terminalId, cancellationToken)
            ?? throw new CashSessionNotFoundException();
    }

    public async Task CloseAsync(
        Guid cashSessionId,
        decimal actualClosingAmount,
        long expectedConcurrencyVersion,
        string subject,
        Guid terminalId,
        CancellationToken cancellationToken)
    {
        RequireSubject(subject);
        if (cashSessionId == Guid.Empty || terminalId == Guid.Empty || actualClosingAmount < 0 ||
            expectedConcurrencyVersion <= 0)
        {
            throw new CashSessionValidationException(
                "Cash session, terminal, reviewed version, and a non-negative closing amount are required.");
        }

        try
        {
            await store.CloseAsync(cashSessionId, actualClosingAmount, expectedConcurrencyVersion,
                subject, terminalId, cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            throw new CashSessionConflictException(exception.Message, exception);
        }
    }

    private static void RequireSubject(string subject)
    {
        if (string.IsNullOrWhiteSpace(subject))
        {
            throw new CashSessionAuthorizationException();
        }
    }

    private static string ComputeMovementHash(RecordCashMovementCommand command)
    {
        string value = string.Join('\n',
            command.CashSessionId.ToString("D"),
            command.MovementType,
            command.Amount.ToString("0.####", CultureInfo.InvariantCulture),
            command.ReasonCode?.Trim() ?? "");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }
}

public sealed class CashSessionValidationException(string message) : Exception(message);
public sealed class CashSessionAuthorizationException() : Exception("An authenticated POS subject is required.");
public sealed class CashSessionNotFoundException() : Exception("The cash session was not found for this terminal and cashier.");
public sealed class CashSessionReplayAuthorizationException() : Exception("The offline operation does not belong to this terminal and shift subject.");
public sealed class CashSessionConflictException(string message, Exception innerException) : Exception(message, innerException);
public sealed class DuplicateSyncOperationException(string message) : Exception(message);
