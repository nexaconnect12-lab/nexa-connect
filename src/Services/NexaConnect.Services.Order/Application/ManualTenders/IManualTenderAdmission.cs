namespace NexaConnect.Services.Order.Application.ManualTenders;

public sealed record ManualTenderAdmissionRequest(Guid OrganizationId, Guid RestaurantId, Guid BranchId,
    Guid TerminalId, string OperatorSubjectId, string Method, string Currency);

// POS owns terminal/shift/drawer facts. This observation is not a distributed lock.
public interface IManualTenderAdmission
{
    Task<DateTimeOffset> AdmitAsync(ManualTenderAdmissionRequest request, CancellationToken cancellationToken);
}

public sealed class ManualTenderAdmissionConflictException : Exception;
