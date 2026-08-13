namespace NexaConnect.Services.Restaurant.Application.Branches;

public sealed record BranchSummary(Guid BranchId, Guid RestaurantId, Guid OrganizationId, string Code, string Name, string TimeZone, string Currency, string Status, DateTimeOffset? OpenedAtUtc, DateTimeOffset? ClosedAtUtc, long ConcurrencyVersion);
public sealed record CreateManagedBranchCommand(Guid RestaurantId, string Code, string Name, string TimeZone, string Currency);
public sealed record UpdateManagedBranchCommand(string Name, string TimeZone, string Currency, string Status, long ExpectedVersion);
public sealed class BranchConflictException(string message) : Exception(message);

public interface IBranchManagementRepository
{
    Task<IReadOnlyCollection<BranchSummary>> ListAsync(Guid organizationId, CancellationToken cancellationToken);
    Task<BranchSummary?> CreateAsync(Guid organizationId, CreateManagedBranchCommand command, string actor, CancellationToken cancellationToken);
    Task<BranchSummary?> UpdateAsync(Guid organizationId, Guid branchId, UpdateManagedBranchCommand command, string actor, CancellationToken cancellationToken);
}

public sealed class BranchManagement(IBranchManagementRepository repository)
{
    private static readonly HashSet<string> Statuses=["active","suspended","closed"];
    public Task<IReadOnlyCollection<BranchSummary>> ListAsync(Guid organizationId,CancellationToken cancellationToken){RequireId(organizationId,"Organization");return repository.ListAsync(organizationId,cancellationToken);}
    public Task<BranchSummary?> CreateAsync(Guid organizationId,CreateManagedBranchCommand command,string actor,CancellationToken cancellationToken){RequireId(organizationId,"Organization");RequireId(command.RestaurantId,"Restaurant");string code=command.Code?.Trim().ToLowerInvariant()??"";Validate(code,command.Name,command.TimeZone,command.Currency,actor);return repository.CreateAsync(organizationId,command with{Code=code,Name=command.Name.Trim(),TimeZone=command.TimeZone.Trim(),Currency=command.Currency.Trim().ToUpperInvariant()},actor.Trim(),cancellationToken);}
    public Task<BranchSummary?> UpdateAsync(Guid organizationId,Guid branchId,UpdateManagedBranchCommand command,string actor,CancellationToken cancellationToken){RequireId(organizationId,"Organization");RequireId(branchId,"Branch");Validate("valid",command.Name,command.TimeZone,command.Currency,actor);string status=command.Status?.Trim().ToLowerInvariant()??"";if(!Statuses.Contains(status))throw new ArgumentException("Invalid branch status.");if(command.ExpectedVersion<=0)throw new ArgumentException("Expected version must be positive.");return repository.UpdateAsync(organizationId,branchId,command with{Name=command.Name.Trim(),TimeZone=command.TimeZone.Trim(),Currency=command.Currency.Trim().ToUpperInvariant(),Status=status},actor.Trim(),cancellationToken);}
    private static void Validate(string code,string name,string timeZone,string currency,string actor){if(string.IsNullOrWhiteSpace(actor)||string.IsNullOrWhiteSpace(name)||string.IsNullOrWhiteSpace(timeZone))throw new ArgumentException("Actor, name, and time zone are required.");if(!System.Text.RegularExpressions.Regex.IsMatch(code?.Trim()??"","^[a-z0-9][a-z0-9_-]{0,63}$"))throw new ArgumentException("Code has an invalid format.");if(!System.Text.RegularExpressions.Regex.IsMatch(currency?.Trim().ToUpperInvariant()??"","^[A-Z]{3}$"))throw new ArgumentException("Currency must be a three-letter ISO code.");}
    private static void RequireId(Guid id,string name){if(id==Guid.Empty)throw new ArgumentException($"{name} identifier is required.");}
}
