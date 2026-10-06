using NexaConnect.Contracts.Reporting;
using NexaConnect.Services.Payment.Application.Tenant;

namespace NexaConnect.Services.Payment.Application.Refunds;

public interface IPaymentDayReader
{
    Task<PaymentDaySummary> ReadAsync(EndOfDayWindow window, CancellationToken ct);
}
public sealed class PaymentDayRead(IPaymentTenantAuthorizer authorizer, IPaymentDayReader? reader = null)
{
    public async Task<PaymentDaySummary> ReadAsync(EndOfDayWindow window, string bearer, CancellationToken ct)
    {
        await AuthorizeAsync(window, bearer, ct);
        return await (reader ?? throw new InvalidOperationException("Durable persistence is required.")).ReadAsync(window, ct);
    }
    public async Task AuthorizeAsync(EndOfDayWindow window, string bearer, CancellationToken ct)
    {
        if (window.OrganizationId == Guid.Empty || window.RestaurantId == Guid.Empty || window.BranchId == Guid.Empty
            || window.FromUtc == default || window.ToUtc <= window.FromUtc
            || window.ToUtc - window.FromUtc > TimeSpan.FromHours(27) || window.ToUtc > DateTimeOffset.UtcNow)
            throw new ArgumentException("A closed branch day is required.");
        if (!await authorizer.CanReadBranchFinancialsAsync(window.OrganizationId, window.RestaurantId, window.BranchId, bearer, ct))
            throw new UnauthorizedAccessException();
    }
}
