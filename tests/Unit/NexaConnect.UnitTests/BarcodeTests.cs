using NexaConnect.POS;
using NexaConnect.Services.Catalog.Application.Barcodes;
using NexaConnect.Services.Catalog.Application.Menu;
using NexaConnect.Services.Catalog.Application.Tenant;
using NexaConnect.Services.Catalog.Domain.Barcodes;
using NexaConnect.Services.Catalog.Infrastructure;

namespace NexaConnect.UnitTests;

public sealed class BarcodeTests
{
    [Theory][InlineData("")][InlineData("123 456")][InlineData("123\n")][InlineData("https://shop.test")][InlineData("ကုန်")]
    public void Invalid_codes_fail_the_server_and_client_contract(string code)
    {Assert.Throws<ArgumentException>(()=>ProductBarcodeValue.Parse(code));Assert.False(BarcodeScanPolicy.Valid(code));}
    [Fact]public void Case_zeroes_versions_and_product_identity_are_preserved()
    {
        Assert.Equal("001a-A",ProductBarcodeValue.Parse("001a-A"));Assert.False(BarcodeScanPolicy.Valid(new string('A',65)));
        Guid product=Guid.NewGuid();
        Assert.Throws<BarcodeConflictException>(()=>BarcodeAssignment.Change(new(1,"old",product),product,"new",0));
        Assert.Throws<BarcodeConflictException>(()=>BarcodeAssignment.Change(new(0,null,Guid.NewGuid()),product,"new",0));
        Assert.Equal(new BarcodeChange(2,null,true),BarcodeAssignment.Change(new(1,"old",null),product,null,1));
        Assert.False(BarcodeAssignment.Change(new(2,null,null),product,null,2).Changed);
    }
    [Fact]public async Task Same_product_can_share_a_code_across_branches_but_other_products_cannot_claim_it()
    {
        var menu=new InMemoryMenuCatalog();var repo=new InMemoryBarcodeRepository(menu);Guid org=Guid.NewGuid(),a=Guid.NewGuid(),b=Guid.NewGuid(),product=Guid.NewGuid(),other=Guid.NewGuid();
        foreach(var branch in new[]{a,b}){menu.AddForOrganizationBranch(org,branch,new(product,"Meal",10,"THB","kitchen"));menu.AddForOrganizationBranch(org,branch,new(other,"Tea",5,"THB","bar"));}
        var service=new BarcodeApplication(repo,new Access());var context=new MenuMutationContext("manager",Guid.NewGuid());
        await service.SetAsync(org,a,product,new("0001",0),"Bearer test",context,default);
        await service.SetAsync(org,b,product,new("0001",0),"Bearer test",context,default);
        await Assert.ThrowsAsync<BarcodeConflictException>(()=>service.SetAsync(org,b,other,new("0001",0),"Bearer test",context,default));
        await service.SetAsync(org,a,product,new(null,1),"Bearer test",context,default);
        Assert.NotNull(await service.LookupAsync(org,b,"0001","Bearer test",default));
        await Assert.ThrowsAsync<KeyNotFoundException>(()=>service.LookupAsync(org,a,"0001","Bearer test",default));
        await Assert.ThrowsAsync<BarcodeConflictException>(()=>service.SetAsync(org,a,other,new("0001",0),"Bearer test",context,default));
        await service.SetAsync(org,b,product,new(null,1),"Bearer test",context,default);
        await service.SetAsync(org,a,other,new("0001",0),"Bearer test",context,default);
        Assert.Equal(other,(await service.LookupAsync(org,a,"0001","Bearer test",default)).Item.ProductId);
    }
    [Fact]public async Task Live_denial_prevents_both_reads_and_writes()
    {
        var service=new BarcodeApplication(null!,new Access{Granted=false});
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>service.LookupAsync(Guid.NewGuid(),Guid.NewGuid(),"001","Bearer",default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>service.SetAsync(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),new("001",0),"Bearer",new("subject",Guid.NewGuid()),default));
    }
    [Theory][InlineData("disabled")][InlineData("inactive")][InlineData("other-tab")][InlineData("locked")][InlineData("no-shift")][InlineData("busy")][InlineData("pending")][InlineData("unsafe")][InlineData("dialog")]
    public void Scan_context_guards_are_fail_closed(string condition)=>Assert.False(BarcodeScanPolicy.CanScan(condition!="disabled",condition!="inactive",condition!="other-tab",condition!="locked",condition!="no-shift",condition=="busy",condition=="pending",condition=="unsafe",condition=="dialog"));
    [Fact]public void Identical_successful_scans_have_a_short_duplicate_guard_but_deliberate_later_scans_work()
    {
        var guard=new BarcodeRepeatGuard();Assert.False(guard.IsRepeat("001",1000));guard.Accepted("001",1000);
        Assert.True(guard.IsRepeat("001",1200));Assert.False(guard.IsRepeat("002",1200));Assert.False(guard.IsRepeat("001",1750));
    }
    private sealed class Access:ICatalogTenantAuthorizer
    {
        public bool Granted=true;
        public Task<bool> HasAccessAsync(Guid org,string permission,string bearer,CancellationToken ct)=>Task.FromResult(Granted);
        public Task<bool> HasBranchAccessAsync(Guid org,Guid branch,string permission,string bearer,CancellationToken ct)=>Task.FromResult(Granted);
    }
}
