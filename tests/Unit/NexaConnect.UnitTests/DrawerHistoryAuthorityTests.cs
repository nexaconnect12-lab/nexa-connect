using NexaConnect.Services.POS.Application.Drawers;
using NexaConnect.Services.POS.Application.Shifts;
using NexaConnect.Services.POS.Domain.Drawers;
namespace NexaConnect.UnitTests;

public sealed class DrawerHistoryAuthorityTests
{
 [Theory][InlineData(0)][InlineData(51)][InlineData(-1)]public void Page_size_is_bounded(int limit)
 {var p=new Ports();Assert.Throws<ArgumentException>(()=>DrawerHistoryQuery.Create(p.Scope,"cashier",p.From,p.To,limit,null,DateTimeOffset.UtcNow));}
 [Fact]public void Cursor_is_bound_to_actor_scope_and_fixed_window()
 {
  var p=new Ports();var query=DrawerHistoryQuery.Create(p.Scope,"cashier",p.From,p.To,1,null,DateTimeOffset.UtcNow);string cursor=query.Cursor(p.Row);
  var next=DrawerHistoryQuery.Create(p.Scope,"cashier",p.From,p.To,1,cursor,DateTimeOffset.UtcNow);Assert.Equal(p.Row.Command.OperationId,next.BeforeOperation);
  Assert.Throws<ArgumentException>(()=>DrawerHistoryQuery.Create(p.Scope,"other",p.From,p.To,1,cursor,DateTimeOffset.UtcNow));
  Assert.Throws<ArgumentException>(()=>DrawerHistoryQuery.Create(p.Scope with{TerminalId=Guid.NewGuid()},"cashier",p.From,p.To,1,cursor,DateTimeOffset.UtcNow));
  Assert.Throws<ArgumentException>(()=>DrawerHistoryQuery.Create(p.Scope,"cashier",p.From.AddHours(-1),p.To,1,cursor,DateTimeOffset.UtcNow));
  foreach(string invalid in new[]{"bad",new string('x',257),"/+/+"})Assert.Throws<ArgumentException>(()=>DrawerHistoryQuery.Create(p.Scope,"cashier",p.From,p.To,1,invalid,DateTimeOffset.UtcNow));
 }
 [Theory][InlineData("long")][InlineData("reverse")][InlineData("future")][InlineData("scope")]
 public void Invalid_history_windows_fail_before_authority(string change)
 {
  var p=new Ports();var from=p.From;var to=p.To;var scope=p.Scope;
  if(change=="long")from=to.AddDays(-32);if(change=="reverse")from=to;if(change=="future")to=DateTimeOffset.UtcNow.AddMinutes(1);if(change=="scope")scope=scope with{StoreId=Guid.Empty};
  Assert.Throws<ArgumentException>(()=>DrawerHistoryQuery.Create(scope,"cashier",from,to,25,null,DateTimeOffset.UtcNow));
 }
 [Fact]public async Task List_and_review_reauthorize_after_io_and_reject_revocation()
 {
  var p=new Ports{RevokeAfterRead=true};var service=new DrawerHistoryReader(p,p,p,TimeProvider.System);
  await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>service.ReadAsync(p.Scope,p.From,p.To,25,null,new("cashier","token"),default));Assert.Equal(1,p.Reads);
  p=new(){Granted=false};service=new(p,p,p,TimeProvider.System);await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>service.ReviewAsync(p.Scope,p.Row.Command.OperationId,new("cashier","token"),default));Assert.Equal(0,p.Reads);
 }
 [Fact]public async Task Foreign_actor_or_scope_from_persistence_never_leaves_application()
 {
  var p=new Ports();p.Row=p.Row with{Actor="other"};var service=new DrawerHistoryReader(p,p,p,TimeProvider.System);
  await Assert.ThrowsAsync<InvalidDataException>(()=>service.ReadAsync(p.Scope,p.From,p.To,25,null,new("cashier","token"),default));
  await Assert.ThrowsAsync<InvalidDataException>(()=>service.ReviewAsync(p.Scope,p.Row.Command.OperationId,new("cashier","token"),default));
 }
 [Fact]public async Task Closed_custody_history_is_read_only_and_returns_original_outcome()
 {
  var p=new Ports();p.Row=p.Row.Report("unknown",DateTimeOffset.UtcNow);var service=new DrawerHistoryReader(p,p,p,TimeProvider.System);
  var page=await service.ReadAsync(p.Scope,p.From,p.To,25,null,new("cashier","token"),default);Assert.Single(page.Items);Assert.Equal("unknown",page.Items[0].Status);Assert.Equal(DrawerIntents.ReadPermission,p.Permission);
  var reviewed=await service.ReviewAsync(p.Scope,p.Row.Command.OperationId,new("cashier","token"),default);Assert.Equal(page.Items[0],reviewed);Assert.Equal("unknown",p.Row.Status);
 }
 private sealed class Ports:IDrawerHistoryRepository,IRestaurantScopeReader,IAuthorizationDecisionClient
 {
  public DrawerHistoryScope Scope=new(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid());public DateTimeOffset From=DateTimeOffset.UtcNow.AddDays(-1),To=DateTimeOffset.UtcNow;
  public DrawerIntent Row;public bool Granted=true,RevokeAfterRead;public int Reads;public string? Permission;
  public Ports(){var now=DateTimeOffset.UtcNow.AddMinutes(-2);Row=new(new(new(Scope.OrganizationId,Scope.RestaurantId,Scope.BranchId,Scope.StoreId,Scope.TerminalId,Guid.NewGuid(),Guid.NewGuid()),Guid.NewGuid(),"manual",null,"cash_check"),"cashier",Guid.NewGuid(),now,now.AddSeconds(60));}
  public Task<bool> MatchesAsync(DrawerHistoryScope s,CancellationToken ct)=>Task.FromResult(s==Scope);
  public Task<IReadOnlyList<DrawerIntent>> ReadAsync(DrawerHistoryQuery q,CancellationToken ct){Reads++;if(RevokeAfterRead)Granted=false;return Task.FromResult<IReadOnlyList<DrawerIntent>>([Row]);}
  public Task<DrawerIntent?> FindHistoryAsync(DrawerHistoryScope s,string actor,Guid op,CancellationToken ct){Reads++;if(RevokeAfterRead)Granted=false;return Task.FromResult<DrawerIntent?>(Row);}
  public Task<RestaurantAuthorizationScope> GetAsync(Guid branch,CancellationToken ct)=>Task.FromResult(new RestaurantAuthorizationScope(Scope.OrganizationId,Scope.RestaurantId,Scope.BranchId));
  public Task<AuthorizationDecision> DecideAsync(PosUserContext u,RestaurantAuthorizationScope s,string permission,CancellationToken ct){Permission=permission;return Task.FromResult(new AuthorizationDecision(Guid.NewGuid(),Granted,null));}
 }
}
