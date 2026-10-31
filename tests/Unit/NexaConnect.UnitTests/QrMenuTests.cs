using System.Text.Json;
using NexaConnect.Contracts.RestaurantQr;
using NexaConnect.Services.Order.Application.Orders;
using NexaConnect.Services.Order.Application.Workflow;
using NexaConnect.Services.Order.Domain;
using NexaConnect.Services.Catalog.Application.Menu;
using NexaConnect.Services.Catalog.Infrastructure;

namespace NexaConnect.UnitTests;

public sealed class QrMenuTests
{
    private const string Token = "SSSSSSSSSSSSSSSSSSSSSSSSSSSSSSSSSSSSSSSSSSS";
    [Fact] public async Task Guest_scope_prices_and_policy_are_authoritative_and_match_staff_resolver()
    {
        var port = new Port(); var policies = new Policies(); var service = new QrMenuService(port, policies, TimeProvider.System);
        var result = await service.QuoteAsync(Token, new([new(port.Item.ProductId, 2)]), default);
        Assert.Equal(port.Context.OrganizationId, port.Organization); Assert.Equal(port.Context.BranchId, port.Branch);
        Assert.Equal((port.Context.OrganizationId, port.Context.RestaurantId, port.Context.BranchId), policies.Scope);
        var expected = OrderPricingService.Resolve(new(port.Organization, port.Branch, [new(port.Item.ProductId, 2)], "THB", "cash", port.Context.RestaurantId),
            policies.Policy, new Dictionary<Guid, CatalogMenuItem> { [port.Item.ProductId] = port.Item });
        Assert.Equal(expected.Fingerprint, result.Fingerprint); Assert.Equal(235.40m, result.TotalAmount);
        Assert.Equal(200m, result.SubtotalAmount); Assert.Equal(20m, result.ServiceChargeAmount); Assert.Equal(15.40m, result.TaxAmount);
        Assert.True(result.ExpiresAtUtc <= DateTimeOffset.UtcNow.AddMinutes(2)); Assert.Equal(2, port.ContextReads);
        string json = JsonSerializer.Serialize(result, JsonSerializerOptions.Web);
        Assert.DoesNotContain("PreparationStation", json, StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain("organizationId", json); Assert.DoesNotContain("stock", json);
    }
    [Fact] public async Task Price_policy_and_availability_are_reread_for_every_quote()
    {
        var port = new Port(); var policies = new Policies(); var service = new QrMenuService(port, policies, TimeProvider.System); var cart = new QrCart([new(port.Item.ProductId, 1)]);
        var first = await service.QuoteAsync(Token, cart, default); port.Item = port.Item with { UnitPrice = 214m };
        var second = await service.QuoteAsync(Token, cart, default); Assert.NotEqual(first.Fingerprint, second.Fingerprint); Assert.Equal(470.80m / 2, second.TotalAmount);
        policies.Policy = policies.Policy with { Version = 2, TaxPercent = 0 }; var third = await service.QuoteAsync(Token, cart, default); Assert.NotEqual(second.Fingerprint, third.Fingerprint);
        port.Item = port.Item with { Available = false }; await Assert.ThrowsAsync<PricingChangedException>(() => service.QuoteAsync(Token, cart, default));
    }
    [Theory][InlineData(0)][InlineData(-1)][InlineData(100)][InlineData(int.MaxValue)]
    public async Task Quantities_are_bounded_before_catalog_work(int quantity)
    {
        var port = new Port(); var service = new QrMenuService(port, new Policies(), TimeProvider.System);
        await Assert.ThrowsAsync<ArgumentException>(() => service.QuoteAsync(Token, new([new(port.Item.ProductId, quantity)]), default)); Assert.Equal(0, port.MenuReads);
    }
    [Fact] public async Task Empty_duplicate_oversized_and_foreign_carts_fail_closed()
    {
        var port = new Port(); var service = new QrMenuService(port, new Policies(), TimeProvider.System);
        foreach(var lines in new IReadOnlyList<QrCartLine>[] { [], [new(port.Item.ProductId, 1), new(port.Item.ProductId, 2)], Enumerable.Range(0,51).Select(_=>new QrCartLine(Guid.NewGuid(),1)).ToArray() })
            await Assert.ThrowsAsync<ArgumentException>(()=>service.QuoteAsync(Token,new(lines),default));
        await Assert.ThrowsAsync<PricingChangedException>(()=>service.QuoteAsync(Token,new([new(Guid.NewGuid(),1)]),default));
    }
    [Fact] public async Task Missing_expired_or_revoked_visitors_do_not_disclose_menu()
    {
        var port=new Port();var service=new QrMenuService(port,new Policies(),TimeProvider.System);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>service.MenuAsync("tampered",default));Assert.Equal(0,port.ContextReads);
        port.Context=port.Context with{ExpiresAtUtc=DateTimeOffset.UtcNow.AddSeconds(-1)};
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>service.MenuAsync(Token,default));Assert.Equal(0,port.MenuReads);
        port.Context=port.Context with{ExpiresAtUtc=DateTimeOffset.UtcNow.AddMinutes(1)};port.RevokeDuringMenu=true;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>service.MenuAsync(Token,default));
    }
    [Fact] public async Task Empty_menu_is_supported_and_oversized_or_invalid_menu_fails_closed()
    {
        var port=new Port();var service=new QrMenuService(port,new Policies(),TimeProvider.System);port.Count=0;Assert.Empty((await service.MenuAsync(Token,default)).Items);
        port.Count=201;await Assert.ThrowsAsync<HttpRequestException>(()=>service.MenuAsync(Token,default));
        port.Count=1;port.Item=port.Item with{Currency="SGD"};await Assert.ThrowsAsync<HttpRequestException>(()=>service.MenuAsync(Token,default));
    }
    [Theory][InlineData("{\"lines\":[],\"currency\":\"SGD\"}")][InlineData("{\"lines\":[{\"productId\":\"11111111-1111-1111-1111-111111111111\",\"quantity\":1,\"unitPrice\":0.01}]}")]
    public void Guest_cannot_supply_price_currency_or_scope(string json) => Assert.Throws<JsonException>(()=>JsonSerializer.Deserialize<QrCart>(json,JsonSerializerOptions.Web));
    [Fact] public void Catalog_projection_is_organization_and_branch_scoped_and_bounded()
    {
        var catalog=new InMemoryMenuCatalog();var org=Guid.NewGuid();var branch=Guid.NewGuid();var product=Guid.NewGuid();
        catalog.AddForOrganizationBranch(org,branch,new(product,"Soup",107,"THB","kitchen"));
        catalog.AddForOrganizationBranch(Guid.NewGuid(),branch,new(product,"Secret",1,"THB","kitchen"));
        var read=new GuestMenuRead(catalog);Assert.Equal("Soup",Assert.Single(read.Read(org,branch).Items).Name);Assert.Empty(read.Read(org,Guid.NewGuid()).Items);
        for(int i=0;i<200;i++)catalog.AddForOrganizationBranch(org,branch,new(Guid.NewGuid(),"Item",1,"THB","kitchen"));
        Assert.Throws<ArgumentException>(()=>read.Read(org,branch));
    }
    [Fact] public async Task Photos_use_live_guest_scope_without_changing_quote_or_allowing_foreign_products()
    {
        var port=new Port();var service=new QrMenuService(port,new Policies(),TimeProvider.System,port);
        var cart=new QrCart([new(port.Item.ProductId,1)]);var before=await service.QuoteAsync(Token,cart,default);
        Assert.Null(await service.PhotoAsync(Token,Guid.NewGuid(),default));Assert.Equal(0,port.PhotoReads);
        Assert.Equal(new byte[]{1,2,3},await service.PhotoAsync(Token,port.Item.ProductId,default));
        Assert.Equal(before.Fingerprint,(await service.QuoteAsync(Token,cart,default)).Fingerprint);
        Assert.Equal((port.Context.OrganizationId,port.Context.BranchId,port.Item.ProductId),port.PhotoScope);
    }
    [Theory][InlineData("revoked")][InlineData("removed")][InlineData("oversized")]
    public async Task Photo_races_and_oversized_bytes_fail_closed(string condition)
    {
        var port=new Port();var service=new QrMenuService(port,new Policies(),TimeProvider.System,port);
        port.AfterPhoto=()=>{if(condition=="revoked")port.Context=port.Context with{VisitorId=Guid.NewGuid()};if(condition=="removed")port.Count=0;};
        if(condition=="oversized")port.PhotoBytes=new byte[524289];
        if(condition=="removed")Assert.Null(await service.PhotoAsync(Token,port.Item.ProductId,default));
        else if(condition=="revoked")await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>service.PhotoAsync(Token,port.Item.ProductId,default));
        else await Assert.ThrowsAsync<HttpRequestException>(()=>service.PhotoAsync(Token,port.Item.ProductId,default));
    }
    [Fact]public async Task Organization_is_guest_scoped_and_never_changes_authoritative_quote_fingerprint()
    {
        var port=new Port();var category=new QrMenuCategory(Guid.NewGuid(),"Food");port.Layout=new([category],[new(port.Item.ProductId,category.Id),new(Guid.NewGuid(),category.Id)]);
        var service=new QrMenuService(port,new Policies(),TimeProvider.System,null,port);var cart=new QrCart([new(port.Item.ProductId,1)]);var before=await service.QuoteAsync(Token,cart,default);
        var layout=await service.OrganizationAsync(Token,default);Assert.Single(layout.Items);Assert.Equal(category.Id,Assert.Single(layout.Categories).Id);
        port.Layout=new([category with{Name="Mains"}],layout.Items);Assert.Equal(before.Fingerprint,(await service.QuoteAsync(Token,cart,default)).Fingerprint);
        port.AfterOrganization=()=>port.Context=port.Context with{VisitorId=Guid.NewGuid()};await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>service.OrganizationAsync(Token,default));
    }
    private sealed class Port : IQrMenuPort,IQrMenuPhotoPort,IQrMenuOrganizationPort
    {
        public QrMenuOrganization Layout=new([],[]);public Action? AfterOrganization;
        public Task<QrMenuOrganization> OrganizationAsync(Guid org,Guid branch,CancellationToken ct){Assert.Equal(Context.OrganizationId,org);Assert.Equal(Context.BranchId,branch);AfterOrganization?.Invoke();return Task.FromResult(Layout);}
        public byte[] PhotoBytes=[1,2,3];public int PhotoReads;public Action? AfterPhoto;public (Guid,Guid,Guid) PhotoScope;
        public Task<byte[]?> PhotoAsync(Guid o,Guid b,Guid p,CancellationToken ct){PhotoScope=(o,b,p);PhotoReads++;AfterPhoto?.Invoke();return Task.FromResult<byte[]?>(PhotoBytes);}
        public QrGuestContext Context=new(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),"R","B","A1","THB",DateTimeOffset.UtcNow.AddMinutes(30));
        public CatalogMenuItem Item=new(Guid.NewGuid(),"Soup",107,"THB",true,"kitchen");public int ContextReads,MenuReads,Count=1;public Guid Organization,Branch;public bool RevokeDuringMenu;
        public Task<QrGuestContext?> ContextAsync(string capability,CancellationToken ct){ContextReads++;return Task.FromResult<QrGuestContext?>(RevokeDuringMenu&&MenuReads>0?null:Context);}
        public Task<IReadOnlyList<CatalogMenuItem>> MenuAsync(Guid organization,Guid branch,CancellationToken ct){Organization=organization;Branch=branch;MenuReads++;return Task.FromResult<IReadOnlyList<CatalogMenuItem>>(Enumerable.Range(0,Count).Select(_=>Item).ToArray());}
    }
    private sealed class Policies : IBranchPricingPort
    {
        public PricingPolicy Policy=new(1,7,true,10);public (Guid,Guid,Guid) Scope;
        public Task<PricingPolicy> GetAsync(Guid organizationId,Guid restaurantId,Guid branchId,CancellationToken ct){Scope=(organizationId,restaurantId,branchId);return Task.FromResult(Policy);}
    }
}
