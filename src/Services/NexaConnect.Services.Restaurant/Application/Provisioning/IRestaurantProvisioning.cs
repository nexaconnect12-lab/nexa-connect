namespace NexaConnect.Services.Restaurant.Application.Provisioning;

public sealed record CreateRestaurantCommand(Guid OrganizationId, string Code, string Name, string Currency, string TimeZone);
public sealed record CreateBranchCommand(string Code, string Name, string Currency, string TimeZone);
public sealed record RestaurantProvisioningResult(Guid RestaurantId, Guid OrganizationId, string Code, string Name);
public sealed record BranchProvisioningResult(Guid BranchId, Guid RestaurantId, Guid OrganizationId, string Code, string Name);

public interface IRestaurantProvisioningRepository
{
    Task<RestaurantProvisioningResult> CreateRestaurantAsync(CreateRestaurantCommand command, string actor, CancellationToken cancellationToken);
    Task<BranchProvisioningResult?> CreateBranchAsync(Guid restaurantId, CreateBranchCommand command, string actor, CancellationToken cancellationToken);
}

public interface IRestaurantProvisioning
{
    Task<RestaurantProvisioningResult> CreateRestaurantAsync(CreateRestaurantCommand command, string actor, CancellationToken cancellationToken);
    Task<BranchProvisioningResult?> CreateBranchAsync(Guid restaurantId, CreateBranchCommand command, string actor, CancellationToken cancellationToken);
}

public sealed class RestaurantProvisioningService(IRestaurantProvisioningRepository repository) : IRestaurantProvisioning
{
    public Task<RestaurantProvisioningResult> CreateRestaurantAsync(CreateRestaurantCommand command, string actor, CancellationToken cancellationToken)
    {
        Validate(command.OrganizationId, command.Code, command.Name, command.Currency, command.TimeZone, actor);
        return repository.CreateRestaurantAsync(command with { Code = command.Code.Trim().ToLowerInvariant(), Name = command.Name.Trim(), Currency = command.Currency.Trim().ToUpperInvariant(), TimeZone = command.TimeZone.Trim() }, actor.Trim(), cancellationToken);
    }

    public Task<BranchProvisioningResult?> CreateBranchAsync(Guid restaurantId, CreateBranchCommand command, string actor, CancellationToken cancellationToken)
    {
        Validate(restaurantId, command.Code, command.Name, command.Currency, command.TimeZone, actor);
        return repository.CreateBranchAsync(restaurantId, command with { Code = command.Code.Trim().ToLowerInvariant(), Name = command.Name.Trim(), Currency = command.Currency.Trim().ToUpperInvariant(), TimeZone = command.TimeZone.Trim() }, actor.Trim(), cancellationToken);
    }

    private static void Validate(Guid ownerId, string code, string name, string currency, string timeZone, string actor)
    {
        if (ownerId == Guid.Empty) throw new ArgumentException("Owner identifier is required.");
        if (string.IsNullOrWhiteSpace(actor) || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(timeZone)) throw new ArgumentException("Actor, name, and time zone are required.");
        if (!System.Text.RegularExpressions.Regex.IsMatch(code?.Trim() ?? string.Empty, "^[a-z0-9][a-z0-9_-]{0,63}$")) throw new ArgumentException("Code has an invalid format.");
        if (!System.Text.RegularExpressions.Regex.IsMatch(currency?.Trim().ToUpperInvariant() ?? string.Empty, "^[A-Z]{3}$")) throw new ArgumentException("Currency must be a three-letter ISO code.");
    }
}
