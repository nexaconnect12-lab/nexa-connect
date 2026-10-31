using System.IO;
using NexaConnect.Contracts.Catalog;
namespace NexaConnect.POS;
public static class PosMenuOrganizationView
{
    public static void Validate(BranchMenuOrganization layout,Guid org,Guid branch)
    {
        if(layout.OrganizationId!=org||layout.BranchId!=branch||layout.Version<0||layout.Categories is null||layout.Items is null||layout.Categories.Count>50||layout.Items.Count>200
            ||layout.Categories.Any(c=>c is null||c.Id==Guid.Empty||string.IsNullOrWhiteSpace(c.Name)||c.Name.Length>80||c.Name.Any(char.IsControl))
            ||layout.Items.Any(i=>i is null||i.ProductId==Guid.Empty||i.CategoryId==Guid.Empty)
            ||layout.Categories.Select(c=>c.Id).Distinct().Count()!=layout.Categories.Count||layout.Categories.Select(c=>c.Name.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=layout.Categories.Count||layout.Items.Select(i=>i.ProductId).Distinct().Count()!=layout.Items.Count
            ||layout.Version==0&&(layout.Categories.Count>0||layout.Items.Count>0)
            ||layout.Items.Any(i=>i.CategoryId is Guid id&&!layout.Categories.Any(c=>c.Id==id)))throw new InvalidDataException("Invalid menu organization.");
    }
    public static Guid? CategoryFor(Guid product,BranchMenuOrganization? layout)=>layout?.Items.FirstOrDefault(i=>i.ProductId==product)?.CategoryId;
    public static IReadOnlyList<PosMenuItem> Order(IReadOnlyList<PosMenuItem> items,BranchMenuOrganization layout)
    {
        var categories=layout.Categories.Select((c,i)=>(c.Id,i)).ToDictionary(x=>x.Id,x=>x.i);
        var placements=layout.Items.Select((p,i)=>(p.ProductId,p.CategoryId,i)).ToDictionary(x=>x.ProductId);
        return items.Select((item,index)=>(item,index)).OrderBy(x=>placements.TryGetValue(x.item.ProductId,out var p)&&p.CategoryId is Guid id&&categories.TryGetValue(id,out var rank)?rank:categories.Count)
            .ThenBy(x=>placements.TryGetValue(x.item.ProductId,out var p)?p.i:layout.Items.Count+x.index).Select(x=>x.item).ToArray();
    }
}
