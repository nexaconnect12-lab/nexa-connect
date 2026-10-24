using NexaConnect.Services.POS.Application.Shifts;

namespace NexaConnect.Services.POS.Application.Terminals;

public sealed record ManualTenderAdmissionRequest(Guid OrganizationId, Guid RestaurantId, Guid BranchId,
    Guid TerminalId, string OperatorSubjectId, string Method, string Currency);
public interface IManualTenderAdmissionStore
{
    Task<bool> IsEligibleAsync(ManualTenderAdmissionRequest request, DateTimeOffset at, CancellationToken ct);
}

public sealed class ManualTenderAdmission(IManualTenderAdmissionStore store, IRestaurantScopeReader hierarchy,
    TimeProvider clock)
{
    public async Task<DateTimeOffset?> AdmitAsync(ManualTenderAdmissionRequest request, CancellationToken ct)
    {
        if (new[] { request.OrganizationId, request.RestaurantId, request.BranchId, request.TerminalId }.Any(id => id == Guid.Empty)
            || string.IsNullOrWhiteSpace(request.OperatorSubjectId) || request.OperatorSubjectId.Length > 200
            || request.Method is not ("cash" or "promptpay_manual") || request.Currency != "THB")
            throw new ArgumentException("Valid terminal scope, cashier and THB manual method are required.");
        var scope = await hierarchy.GetAsync(request.BranchId, ct);
        if (scope.OrganizationId != request.OrganizationId || scope.RestaurantId != request.RestaurantId
            || scope.BranchId != request.BranchId) return null;
        var at = clock.GetUtcNow();
        return await store.IsEligibleAsync(request, at, ct) ? at : null;
    }
}
