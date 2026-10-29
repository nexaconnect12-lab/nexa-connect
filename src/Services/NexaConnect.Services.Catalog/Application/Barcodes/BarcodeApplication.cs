using NexaConnect.Contracts.Catalog;
using NexaConnect.Contracts.Platform;
using NexaConnect.Services.Catalog.Application.Menu;
using NexaConnect.Services.Catalog.Application.Tenant;
using NexaConnect.Services.Catalog.Domain.Barcodes;

namespace NexaConnect.Services.Catalog.Application.Barcodes;

public sealed record BarcodeMatch(Guid OrganizationId, Guid BranchId, string Barcode, MenuItem Item);
public interface IBarcodeRepository
{
    Task<ProductBarcode?> ReadAsync(Guid org, Guid branch, Guid product, CancellationToken ct);
    Task<MenuItem?> LookupAsync(Guid org, Guid branch, string barcode, CancellationToken ct);
    // Implementations serialize the organization's assignments and invoke the Domain decision inside one transaction.
    Task<ProductBarcode> ChangeAsync(Guid org, Guid branch, Guid product, string? barcode,
        Func<BarcodeWriteState, BarcodeChange> decide, MenuMutationContext context, CancellationToken ct);
}
public sealed class BarcodeApplication(IBarcodeRepository repository, ICatalogTenantAuthorizer authorizer)
{
    private async Task Require(Guid org, Guid branch, string permission, string bearer, CancellationToken ct)
    {
        if (org == Guid.Empty || branch == Guid.Empty || !await authorizer.HasBranchAccessAsync(org, branch, permission, bearer, ct))
            throw new UnauthorizedAccessException();
    }
    public async Task<ProductBarcode> ReadAsync(Guid org, Guid branch, Guid product, string bearer, CancellationToken ct)
    {
        await Require(org, branch, ProductPermissions.CatalogMenuRead, bearer, ct);
        return await repository.ReadAsync(org, branch, product, ct) ?? throw new KeyNotFoundException();
    }
    public async Task<ProductBarcode> SetAsync(Guid org, Guid branch, Guid product, SetProductBarcode update, string bearer, MenuMutationContext context, CancellationToken ct)
    {
        await Require(org, branch, ProductPermissions.CatalogMenuWrite, bearer, ct);
        if (product == Guid.Empty || update.ExpectedVersion < 0 || string.IsNullOrWhiteSpace(context.ActorSubjectId)
            || context.ActorSubjectId.Length > 256 || context.ActorSubjectId.Any(char.IsControl) || context.CorrelationId == Guid.Empty)
            throw new ArgumentException("A product, current version and verified actor are required.");
        if (update.Barcode is not null) ProductBarcodeValue.Parse(update.Barcode);
        return await repository.ChangeAsync(org, branch, product, update.Barcode,
            state => BarcodeAssignment.Change(state, product, update.Barcode, update.ExpectedVersion), context, ct);
    }
    public async Task<BarcodeMatch> LookupAsync(Guid org, Guid branch, string code, string bearer, CancellationToken ct)
    {
        await Require(org, branch, ProductPermissions.CatalogMenuRead, bearer, ct);
        code = ProductBarcodeValue.Parse(code);
        var item = await repository.LookupAsync(org, branch, code, ct) ?? throw new KeyNotFoundException();
        if (string.IsNullOrWhiteSpace(item.Name) || item.Name.Length > 200 || item.PreparationStation is null || item.PreparationStation.Length > 64
            || item.ProductId == Guid.Empty || item.UnitPrice < 0 || item.Currency is null || item.Currency.Length != 3)
            throw new InvalidDataException("Barcode menu snapshot is invalid.");
        return new(org, branch, code, item);
    }
}
