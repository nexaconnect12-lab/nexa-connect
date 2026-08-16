namespace NexaConnect.Services.Catalog.Application.Menu;

public sealed record MenuItem(Guid ProductId, string Name, decimal UnitPrice, string Currency, string PreparationStation, bool Available);
public sealed record CreateMenuItem(Guid ProductId, string Name, decimal UnitPrice, string Currency, string PreparationStation);
public sealed record MenuMutationContext(string ActorSubjectId, Guid CorrelationId);

public interface IMenuCatalog
{
    IReadOnlyCollection<MenuItem> GetForBranch(Guid branchId);
    IReadOnlyCollection<MenuItem> GetForOrganizationBranch(Guid organizationId, Guid branchId);
    MenuItem Add(Guid branchId, CreateMenuItem command, MenuMutationContext? context = null);
    MenuItem AddForOrganizationBranch(Guid organizationId, Guid branchId, CreateMenuItem command, MenuMutationContext? context = null);
    Task<bool> ProductExistsAsync(Guid organizationId, Guid productId, CancellationToken cancellationToken);
}
