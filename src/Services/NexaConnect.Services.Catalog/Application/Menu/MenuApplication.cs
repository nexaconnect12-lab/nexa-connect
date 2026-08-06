namespace NexaConnect.Services.Catalog.Application.Menu;

public sealed record MenuItem(Guid ProductId, string Name, decimal UnitPrice, string Currency, string PreparationStation, bool Available);
public sealed record CreateMenuItem(Guid ProductId, string Name, decimal UnitPrice, string Currency, string PreparationStation);

public interface IMenuCatalog
{
    IReadOnlyCollection<MenuItem> GetForBranch(Guid branchId);
    MenuItem Add(Guid branchId, CreateMenuItem command);
}
