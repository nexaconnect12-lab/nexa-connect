using NexaConnect.Contracts.RestaurantQr;

namespace NexaConnect.Services.Catalog.Application.Menu;

public sealed class GuestMenuRead(IMenuCatalog catalog)
{
    public QrCatalogMenu Read(Guid organization, Guid branch)
    {
        if (organization == Guid.Empty || branch == Guid.Empty) throw new ArgumentException("Invalid menu scope.");
        var items = catalog.GetGuestMenu(organization, branch);
        if (items.Count > 200 || items.Any(i => string.IsNullOrWhiteSpace(i.Name) || i.Name.Length > 200 || i.PreparationStation.Length > 64))
            throw new ArgumentException("Guest menu exceeds supported bounds.");
        // Private Order workload projection; Order removes station from public responses.
        return new(items.Select(i => new QrCatalogItem(i.ProductId, i.Name, i.UnitPrice, i.Currency, i.Available, i.PreparationStation)).ToArray());
    }
}
