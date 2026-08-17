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
    Guid? ClientOperationId = null);

public interface ICashSessionStore
{
    Task<Guid> OpenAsync(Guid shiftId, Guid storeId, string currency, decimal openingAmount, CancellationToken cancellationToken);
    Task<bool> RecordMovementAsync(Guid cashSessionId, string movementType, decimal amount, string recordedBy, string? reasonCode, Guid? clientOperationId, string payloadHash, CancellationToken cancellationToken);
    Task CloseAsync(Guid cashSessionId, decimal actualClosingAmount, CancellationToken cancellationToken);
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

    public async Task RecordMovementAsync(
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

        try
        {
            await store.RecordMovementAsync(
                command.CashSessionId,
                command.MovementType,
                command.Amount,
                subject,
                command.ReasonCode,
                command.ClientOperationId,
                ComputeMovementHash(command),
                cancellationToken);
        }
        catch (DuplicateSyncOperationException exception)
        {
            throw new CashSessionConflictException(exception.Message, exception);
        }
        catch (InvalidOperationException exception)
        {
            throw new CashSessionConflictException(exception.Message, exception);
        }
    }

    public async Task CloseAsync(
        Guid cashSessionId,
        decimal actualClosingAmount,
        string subject,
        CancellationToken cancellationToken)
    {
        RequireSubject(subject);
        if (cashSessionId == Guid.Empty || actualClosingAmount < 0)
        {
            throw new CashSessionValidationException("Cash session and a non-negative closing amount are required.");
        }

        try
        {
            await store.CloseAsync(cashSessionId, actualClosingAmount, cancellationToken);
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
public sealed class CashSessionConflictException(string message, Exception innerException) : Exception(message, innerException);
public sealed class DuplicateSyncOperationException(string message) : Exception(message);
