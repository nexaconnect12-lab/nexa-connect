namespace NexaConnect.Services.POS.Domain.Shifts;

public enum ShiftStatus
{
    Open,
    Closed
}

public sealed class Shift
{
    private Shift(
        Guid id,
        Guid storeId,
        Guid terminalId,
        string employeeSubject,
        string shiftNumber,
        ShiftStatus status,
        DateTimeOffset openedAtUtc,
        DateTimeOffset? closedAtUtc,
        string openedBy,
        string? closedBy,
        Guid authorizationDecisionId,
        Guid? closeAuthorizationDecisionId,
        long concurrencyVersion)
    {
        Id = id;
        StoreId = storeId;
        TerminalId = terminalId;
        EmployeeSubject = employeeSubject;
        ShiftNumber = shiftNumber;
        Status = status;
        OpenedAtUtc = openedAtUtc;
        ClosedAtUtc = closedAtUtc;
        OpenedBy = openedBy;
        ClosedBy = closedBy;
        AuthorizationDecisionId = authorizationDecisionId;
        CloseAuthorizationDecisionId = closeAuthorizationDecisionId;
        ConcurrencyVersion = concurrencyVersion;
    }

    public Guid Id { get; }
    public Guid StoreId { get; }
    public Guid TerminalId { get; }
    public string EmployeeSubject { get; }
    public string ShiftNumber { get; }
    public ShiftStatus Status { get; private set; }
    public DateTimeOffset OpenedAtUtc { get; }
    public DateTimeOffset? ClosedAtUtc { get; private set; }
    public string OpenedBy { get; }
    public string? ClosedBy { get; private set; }
    public Guid AuthorizationDecisionId { get; }
    public Guid? CloseAuthorizationDecisionId { get; private set; }
    public long ConcurrencyVersion { get; private set; }

    public static Shift Open(
        Guid id,
        Guid storeId,
        Guid terminalId,
        string employeeSubject,
        string shiftNumber,
        Guid authorizationDecisionId,
        DateTimeOffset openedAtUtc)
    {
        if (id == Guid.Empty || storeId == Guid.Empty || terminalId == Guid.Empty)
        {
            throw new ShiftValidationException("Shift identifiers are required.");
        }

        if (string.IsNullOrWhiteSpace(employeeSubject) || string.IsNullOrWhiteSpace(shiftNumber))
        {
            throw new ShiftValidationException("Employee subject and shift number are required.");
        }

        if (authorizationDecisionId == Guid.Empty)
        {
            throw new ShiftValidationException("An authorization decision is required to open a shift.");
        }

        return new Shift(
            id,
            storeId,
            terminalId,
            employeeSubject.Trim(),
            shiftNumber.Trim(),
            ShiftStatus.Open,
            openedAtUtc,
            null,
            employeeSubject.Trim(),
            null,
            authorizationDecisionId,
            null,
            1);
    }

    public static Shift Rehydrate(
        Guid id,
        Guid storeId,
        Guid terminalId,
        string employeeSubject,
        string shiftNumber,
        ShiftStatus status,
        DateTimeOffset openedAtUtc,
        DateTimeOffset? closedAtUtc,
        string openedBy,
        string? closedBy,
        Guid authorizationDecisionId,
        Guid? closeAuthorizationDecisionId,
        long concurrencyVersion) => new(
            id,
            storeId,
            terminalId,
            employeeSubject,
            shiftNumber,
            status,
            openedAtUtc,
            closedAtUtc,
            openedBy,
            closedBy,
            authorizationDecisionId,
            closeAuthorizationDecisionId,
            concurrencyVersion);

    public void Close(string closedBy, Guid closeAuthorizationDecisionId, DateTimeOffset closedAtUtc)
    {
        if (Status != ShiftStatus.Open)
        {
            throw new ShiftStateException("Only an open shift can be closed.");
        }

        if (string.IsNullOrWhiteSpace(closedBy) || closeAuthorizationDecisionId == Guid.Empty)
        {
            throw new ShiftValidationException("Closing subject and authorization decision are required.");
        }

        Status = ShiftStatus.Closed;
        ClosedBy = closedBy.Trim();
        CloseAuthorizationDecisionId = closeAuthorizationDecisionId;
        ClosedAtUtc = closedAtUtc;
        ConcurrencyVersion++;
    }
}

public sealed class ShiftValidationException(string message) : Exception(message);

public sealed class ShiftStateException(string message) : Exception(message);
