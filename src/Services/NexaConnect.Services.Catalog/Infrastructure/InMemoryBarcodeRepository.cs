using NexaConnect.Contracts.Catalog;
using NexaConnect.Services.Catalog.Application.Barcodes;
using NexaConnect.Services.Catalog.Application.Menu;
using NexaConnect.Services.Catalog.Domain.Barcodes;

namespace NexaConnect.Services.Catalog.Infrastructure;

public sealed class InMemoryBarcodeRepository(IMenuCatalog menu):IBarcodeRepository
{
    private readonly object gate=new();
    private readonly Dictionary<(Guid Org,Guid Branch,Guid Product),ProductBarcode> bindings=[];
    public Task<ProductBarcode?> ReadAsync(Guid org,Guid branch,Guid product,CancellationToken ct)
    {
        lock(gate)return Task.FromResult(menu.GetForOrganizationBranch(org,branch).Any(p=>p.ProductId==product)
            ? bindings.GetValueOrDefault((org,branch,product))??new ProductBarcode(org,branch,product,null,0):null);
    }
    public Task<MenuItem?> LookupAsync(Guid org,Guid branch,string barcode,CancellationToken ct)
    {
        lock(gate){var binding=bindings.Values.FirstOrDefault(b=>b.OrganizationId==org&&b.BranchId==branch&&b.Barcode==barcode);
            return Task.FromResult(binding is null?null:menu.GetForOrganizationBranch(org,branch).FirstOrDefault(p=>p.ProductId==binding.ProductId));}
    }
    public Task<ProductBarcode> ChangeAsync(Guid org,Guid branch,Guid product,string? barcode,Func<BarcodeWriteState,BarcodeChange> decide,MenuMutationContext context,CancellationToken ct)
    {
        lock(gate){
            if(!menu.GetForOrganizationBranch(org,branch).Any(p=>p.ProductId==product))throw new KeyNotFoundException();
            var current=bindings.GetValueOrDefault((org,branch,product))??new(org,branch,product,null,0);
            var owner=barcode is null?null:bindings.Values.FirstOrDefault(b=>b.OrganizationId==org&&b.Barcode==barcode);
            var change=decide(new(current.Version,current.Barcode,owner?.ProductId));
            var result=change.Changed?current with{Barcode=change.Barcode,Version=change.Version}:current;
            if(change.Changed)bindings[(org,branch,product)]=result;
            return Task.FromResult(result);
        }
    }
}
