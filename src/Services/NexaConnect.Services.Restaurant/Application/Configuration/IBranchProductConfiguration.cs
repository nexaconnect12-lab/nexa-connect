namespace NexaConnect.Services.Restaurant.Application.Configuration;

public sealed record BranchProductConfiguration(
    Guid BranchId,
    Guid RestaurantId,
    Guid OrganizationId,
    bool DineInEnabled,
    bool TakeawayEnabled,
    bool RequireTableForDineIn,
    decimal ServiceChargePercent,
    long ConcurrencyVersion);

public sealed record UpdateBranchProductConfigurationCommand(
    bool DineInEnabled,
    bool TakeawayEnabled,
    bool RequireTableForDineIn,
    decimal ServiceChargePercent,
    long ExpectedVersion);

public interface IBranchProductConfigurationRepository
{
    Task<BranchProductConfiguration?> GetAsync(Guid organizationId, Guid branchId, CancellationToken cancellationToken);
    Task<BranchProductConfiguration?> UpdateAsync(Guid organizationId, Guid branchId, UpdateBranchProductConfigurationCommand command, string actor, CancellationToken cancellationToken);
}

public sealed class BranchProductConfigurationService(IBranchProductConfigurationRepository repository)
{
    public Task<BranchProductConfiguration?> GetAsync(Guid organizationId, Guid branchId, CancellationToken cancellationToken)
    {
        RequireIds(organizationId, branchId);
        return repository.GetAsync(organizationId, branchId, cancellationToken);
    }

    public Task<BranchProductConfiguration?> UpdateAsync(Guid organizationId, Guid branchId, UpdateBranchProductConfigurationCommand command, string actor, CancellationToken cancellationToken)
    {
        RequireIds(organizationId, branchId);
        if (string.IsNullOrWhiteSpace(actor)) throw new ArgumentException("Actor is required.");
        if (command.ExpectedVersion <= 0) throw new ArgumentException("Expected version must be positive.");
        if (command.ServiceChargePercent is < 0 or > 100) throw new ArgumentException("Service charge percent must be between 0 and 100.");
        if (!command.DineInEnabled && !command.TakeawayEnabled) throw new ArgumentException("At least one service mode must be enabled.");
        if (command.RequireTableForDineIn && !command.DineInEnabled) throw new ArgumentException("Table requirement needs dine-in to be enabled.");
        return repository.UpdateAsync(organizationId, branchId, command, actor.Trim(), cancellationToken);
    }

    private static void RequireIds(Guid organizationId, Guid branchId)
    {
        if (organizationId == Guid.Empty || branchId == Guid.Empty) throw new ArgumentException("Organization and branch identifiers are required.");
    }
}
