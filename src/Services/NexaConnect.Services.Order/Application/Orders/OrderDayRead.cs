using NexaConnect.Contracts.Reporting;
using NexaConnect.Services.Order.Application.Tenant;

namespace NexaConnect.Services.Order.Application.Orders;

public interface IOrderDayReader
{
    Task<OrderDaySummary> ReadAsync(EndOfDayWindow window, CancellationToken ct);
}
public sealed class OrderDayRead(IOrderTenantAuthorizer authorizer, IOrderDayReader? reader = null)
{
    public async Task<OrderDaySummary> ReadAsync(EndOfDayWindow window, string bearer, CancellationToken ct)
    {
        if (window.OrganizationId == Guid.Empty || window.RestaurantId == Guid.Empty || window.BranchId == Guid.Empty
            || window.FromUtc == default || window.ToUtc <= window.FromUtc
            || window.ToUtc - window.FromUtc > TimeSpan.FromHours(27) || window.ToUtc > DateTimeOffset.UtcNow)
            throw new ArgumentException("A closed branch day is required.");
        if (!await authorizer.HasBranchFinancialAccessAsync(window.OrganizationId, window.RestaurantId, window.BranchId, bearer, ct))
            throw new UnauthorizedAccessException();
        return await (reader ?? throw new InvalidOperationException("Durable Order persistence is required.")).ReadAsync(window, ct);
    }
}
