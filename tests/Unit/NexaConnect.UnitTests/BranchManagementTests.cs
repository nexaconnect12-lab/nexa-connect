using NexaConnect.Services.Restaurant.Application.Branches;
namespace NexaConnect.UnitTests;
public sealed class BranchManagementTests
{
 [Fact]public async Task Create_normalizes_code_currency_and_text(){var r=new Repo();var s=new BranchManagement(r);await s.CreateAsync(Guid.NewGuid(),new(Guid.NewGuid()," MAIN "," Main "," Asia/Singapore "," sgd ")," actor ",default);Assert.Equal("main",r.Create!.Code);Assert.Equal("SGD",r.Create.Currency);}
 [Fact]public async Task Update_requires_positive_version_and_valid_status(){var s=new BranchManagement(new Repo());await Assert.ThrowsAsync<ArgumentException>(()=>s.UpdateAsync(Guid.NewGuid(),Guid.NewGuid(),new("Name","UTC","USD","bad",1),"actor",default));await Assert.ThrowsAsync<ArgumentException>(()=>s.UpdateAsync(Guid.NewGuid(),Guid.NewGuid(),new("Name","UTC","USD","active",0),"actor",default));}
 private sealed class Repo:IBranchManagementRepository{public CreateManagedBranchCommand? Create;public Task<IReadOnlyCollection<BranchSummary>> ListAsync(Guid o,CancellationToken c)=>Task.FromResult<IReadOnlyCollection<BranchSummary>>([]);public Task<BranchSummary?> CreateAsync(Guid o,CreateManagedBranchCommand x,string a,CancellationToken c){Create=x;return Task.FromResult<BranchSummary?>(new(Guid.NewGuid(),x.RestaurantId,o,x.Code,x.Name,x.TimeZone,x.Currency,"active",null,null,1));}public Task<BranchSummary?> UpdateAsync(Guid o,Guid b,UpdateManagedBranchCommand x,string a,CancellationToken c)=>Task.FromResult<BranchSummary?>(null);}
}
