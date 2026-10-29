extern alias POS;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NexaConnect.Contracts.POS;
using POS::NexaConnect.Services.POS.Application.Drawers;
using POS::NexaConnect.Services.POS.Application.Shifts;
using POS::NexaConnect.Services.POS.Controllers;
using POS::NexaConnect.Services.POS.Domain.Drawers;
namespace NexaConnect.IntegrationTests;

public sealed class DrawerIntentBoundaryTests
{
 [Theory][InlineData(null,false,401)][InlineData("cashier",false,503)][InlineData("cashier",true,200)]
 public async Task Authority_is_authenticated_and_explicitly_enabled(string? subject,bool enabled,int expected)
 {
  var p=new Ports();await using var app=await Host(p,enabled);using var client=app.GetTestClient();Identify(client,subject);
  using var response=await client.PostAsJsonAsync(Path(p),p.Input);Assert.Equal(expected,(int)response.StatusCode);
  Assert.Equal(expected==200?1:0,p.Reservations);if(expected==200){Assert.True(response.Headers.CacheControl?.NoStore);Assert.True((await response.Content.ReadFromJsonAsync<DrawerIntentV1>())!.MaySubmit);}
 }
 [Fact]public async Task Live_denial_scope_mismatch_and_invalid_reason_never_create_an_intent()
 {
  var p=new Ports();await using var app=await Host(p,true);using var client=app.GetTestClient();Identify(client,"cashier");
  p.Granted=false;using var denied=await client.PostAsJsonAsync(Path(p),p.Input);Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
  p.Granted=true;using var wrong=await client.PostAsJsonAsync(Path(p),p.Input with{OrganizationId=Guid.NewGuid()});Assert.Equal(HttpStatusCode.BadRequest,wrong.StatusCode);
  using var reason=await client.PostAsJsonAsync(Path(p),p.Input with{Reason="arbitrary free text"});Assert.Equal(HttpStatusCode.BadRequest,reason.StatusCode);Assert.Equal(0,p.Reservations);
 }
 [Fact]public async Task Retry_read_and_terminal_reports_never_renew_submission_authority()
 {
  var p=new Ports();await using var app=await Host(p,true);using var client=app.GetTestClient();Identify(client,"cashier");
  using var first=await client.PostAsJsonAsync(Path(p),p.Input);Assert.True((await first.Content.ReadFromJsonAsync<DrawerIntentV1>())!.MaySubmit);
  using var retry=await client.PostAsJsonAsync(Path(p),p.Input);Assert.False((await retry.Content.ReadFromJsonAsync<DrawerIntentV1>())!.MaySubmit);
  using var read=await client.GetAsync(Path(p)+"/"+p.Input.OperationId);Assert.False((await read.Content.ReadFromJsonAsync<DrawerIntentV1>())!.MaySubmit);
  using var report=await client.PostAsJsonAsync(Path(p)+"/"+p.Input.OperationId+"/outcome",new DrawerOutcomeRequestV1(p.Input.OrganizationId,"unknown"));Assert.Equal(HttpStatusCode.OK,report.StatusCode);
  using var changed=await client.PostAsJsonAsync(Path(p)+"/"+p.Input.OperationId+"/outcome",new DrawerOutcomeRequestV1(p.Input.OrganizationId,"submitted"));Assert.Equal(HttpStatusCode.Conflict,changed.StatusCode);
  client.DefaultRequestHeaders.Remove("X-Test-Subject");client.DefaultRequestHeaders.Add("X-Test-Subject","different");using var other=await client.GetAsync(Path(p)+"/"+p.Input.OperationId);Assert.Equal(HttpStatusCode.Forbidden,other.StatusCode);
 }
 [Fact]public async Task Unavailable_dependency_has_safe_bounded_error_and_no_reservation()
 {
  var p=new Ports{Unavailable=true};await using var app=await Host(p,true);using var client=app.GetTestClient();Identify(client,"cashier");
  using var response=await client.PostAsJsonAsync(Path(p),p.Input);Assert.Equal(HttpStatusCode.ServiceUnavailable,response.StatusCode);Assert.DoesNotContain("private dependency",await response.Content.ReadAsStringAsync());Assert.Equal(0,p.Reservations);
 }
 private static string Path(Ports p)=>$"/api/pos/v1/customer/organizations/{p.Input.OrganizationId}/drawer-intents";
 private static void Identify(HttpClient c,string? subject){if(subject is not null){c.DefaultRequestHeaders.Authorization=new("Bearer","synthetic");c.DefaultRequestHeaders.Add("X-Test-Subject",subject);}}
 private static async Task<WebApplication> Host(Ports p,bool enabled)
 {
  var b=WebApplication.CreateBuilder();b.WebHost.UseTestServer();b.Services.AddLogging(l=>l.ClearProviders());b.Services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions,Identity>("Test",_=>{});b.Services.AddAuthorization();
  b.Services.AddControllers().AddApplicationPart(typeof(DrawerIntentsController).Assembly);b.Services.Configure<DrawerIntentOptions>(o=>o.Enabled=enabled);
  b.Services.AddSingleton<IDrawerIntentRepository>(p);b.Services.AddSingleton<IRestaurantScopeReader>(p);b.Services.AddSingleton<IAuthorizationDecisionClient>(p);b.Services.AddScoped<DrawerIntents>();
  var app=b.Build();app.UseAuthentication();app.UseAuthorization();app.MapControllers();await app.StartAsync();return app;
 }
 private sealed class Identity(IOptionsMonitor<AuthenticationSchemeOptions> o,ILoggerFactory l,UrlEncoder e):AuthenticationHandler<AuthenticationSchemeOptions>(o,l,e)
 {
  protected override Task<AuthenticateResult> HandleAuthenticateAsync()=>Task.FromResult(Request.Headers.ContainsKey("X-Test-Subject")?AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub",Request.Headers["X-Test-Subject"].ToString()),new Claim(ClaimTypes.Role,"customer-user")],Scheme.Name)),Scheme.Name)):AuthenticateResult.NoResult());
 }
 private sealed class Ports:IDrawerIntentRepository,IRestaurantScopeReader,IAuthorizationDecisionClient
 {
  public readonly DrawerIntentRequestV1 Input=new(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),"manual",null,"cash_check");public bool Granted=true,Unavailable;public int Reservations;private DrawerIntent? saved;
  public Task<(DrawerIntent,bool)> ReserveAsync(DrawerCommand c,string actor,Guid decision,Func<DrawerCustody?,DateTimeOffset,DrawerIntent> create,CancellationToken ct){Reservations++;bool first=saved is null;saved??=create(new("active","active","open","open","THB","cashier",DateTimeOffset.UtcNow.AddMinutes(-1),false,DateTimeOffset.UtcNow.AddMinutes(-1),false,false,null),DateTimeOffset.UtcNow);saved.RequireSame(c,actor);return Task.FromResult((saved,first));}
  public Task<DrawerIntent?> FindAsync(Guid org,Guid op,CancellationToken ct)=>Task.FromResult(saved is {} s&&s.Command.Scope.OrganizationId==org&&s.Command.OperationId==op?saved:null);
  public Task<DrawerIntent> ReportAsync(DrawerIntent original,string outcome,Guid decision,CancellationToken ct){saved=original.Report(outcome,DateTimeOffset.UtcNow);return Task.FromResult(saved);}
  public Task<RestaurantAuthorizationScope> GetAsync(Guid branch,CancellationToken ct){if(Unavailable)throw new HttpRequestException("private dependency details");return Task.FromResult(new RestaurantAuthorizationScope(Input.OrganizationId,Input.RestaurantId,Input.BranchId));}
  public Task<AuthorizationDecision> DecideAsync(PosUserContext u,RestaurantAuthorizationScope s,string permission,CancellationToken ct)=>Task.FromResult(new AuthorizationDecision(Guid.NewGuid(),Granted,null));
 }
}
