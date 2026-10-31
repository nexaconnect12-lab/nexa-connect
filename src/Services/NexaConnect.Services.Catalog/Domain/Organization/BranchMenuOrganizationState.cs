namespace NexaConnect.Services.Catalog.Domain.Organization;
public sealed record Category(Guid Id,string Name);
public sealed record Placement(Guid ProductId,Guid? CategoryId);
public sealed class MenuOrganizationConflictException:Exception;

// A branch's presentation layout. No price, availability or financial snapshot belongs here.
public sealed class BranchMenuOrganizationState
{
    public long Version{get;}
    public IReadOnlyList<Category> Categories{get;}
    public IReadOnlyList<Placement> Items{get;}
    private BranchMenuOrganizationState(long version,Category[] categories,Placement[] items)
    {Version=version;Categories=Array.AsReadOnly(categories);Items=Array.AsReadOnly(items);}
    public static BranchMenuOrganizationState Empty()=>new(0,[],[]);
    public static BranchMenuOrganizationState Restore(long version,IEnumerable<Category>? categories,IEnumerable<Placement>? items)
    {
        if(version<0||categories is null||items is null)throw new ArgumentException("Invalid menu organization.");
        var c=categories.Take(51).ToArray();var p=items.Take(201).ToArray();
        if(c.Length>50||p.Length>200||c.Any(x=>x is null||x.Id==Guid.Empty||string.IsNullOrWhiteSpace(x.Name)||x.Name.Trim().Length>80||x.Name.Any(char.IsControl))
            ||p.Any(x=>x is null||x.ProductId==Guid.Empty||x.CategoryId==Guid.Empty))throw new ArgumentException("Invalid menu organization.");
        c=c.Select(x=>x with{Name=x.Name.Trim()}).ToArray();
        if(c.Select(x=>x.Id).Distinct().Count()!=c.Length||c.Select(x=>x.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=c.Length
            ||p.Select(x=>x.ProductId).Distinct().Count()!=p.Length||p.Any(x=>x.CategoryId is Guid id&&!c.Any(y=>y.Id==id))
            ||version==0&&(c.Length!=0||p.Length!=0))throw new ArgumentException("Invalid menu organization.");
        return new(version,c,p);
    }
    public BranchMenuOrganizationState Change(long expected,IEnumerable<Category>? categories,IEnumerable<Placement>? items,IReadOnlySet<Guid> knownProducts)
    {
        if(expected<0)throw new ArgumentException("Invalid version.");
        if(expected!=Version)throw new MenuOrganizationConflictException();
        var proposed=Restore(Math.Max(1,Version),categories,items);
        if(proposed.Items.Any(x=>!knownProducts.Contains(x.ProductId)))throw new ArgumentException("Choose products from this branch.");
        if(Categories.SequenceEqual(proposed.Categories)&&Items.SequenceEqual(proposed.Items))return this;
        return new(checked(Version+1),proposed.Categories.ToArray(),proposed.Items.ToArray());
    }
}
