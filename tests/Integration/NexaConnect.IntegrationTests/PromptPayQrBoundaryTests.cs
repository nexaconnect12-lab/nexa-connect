extern alias RESTAURANT;
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
using NexaConnect.Contracts.Media;
using NexaConnect.Infrastructure.Authentication;
using RESTAURANT::NexaConnect.Services.Restaurant.Application.Configuration;
using RESTAURANT::NexaConnect.Services.Restaurant.Application.Branches;
using RESTAURANT::NexaConnect.Services.Restaurant.Domain;
namespace NexaConnect.IntegrationTests;

public sealed class PromptPayQrBoundaryTests
{
 [Theory][InlineData(null,401)][InlineData("cashier",200)][InlineData("owner",200)]
 public async Task Read_requires_authentication_and_live_branch_read(string? identity,int expected)
 {
  var p=new Ports();await using var app=await Host(p);using var client=app.GetTestClient();Identify(client,identity);
  using var response=await client.GetAsync($"/api/restaurant/v1/customer/organizations/{p.Org}/branches/{p.Branch}/promptpay-qr");
  Assert.Equal(expected,(int)response.StatusCode);if(expected==200)Assert.True(response.Headers.CacheControl?.NoStore);
  p.Granted=false;if(identity is not null){using var denied=await client.GetAsync($"/api/restaurant/v1/customer/organizations/{p.Org}/branches/{p.Branch}/promptpay-qr");Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);}
 }
 [Theory][InlineData(null,401)][InlineData("cashier",403)][InlineData("owner",200)][InlineData("manager",200)]
 public async Task Only_live_branch_configuration_authority_can_approve(string? identity,int expected)
 {
  var p=new Ports{ConfigGranted=identity!="cashier"};await using var app=await Host(p);using var client=app.GetTestClient();Identify(client,identity);
  var path=$"/api/restaurant/v1/customer/organizations/{p.Org}/branches/{p.Branch}/promptpay-qr";
  using var response=await client.PutAsJsonAsync(path,new UpdateBranchPromptPayQrV1(1,p.Asset,true));Assert.Equal(expected,(int)response.StatusCode);Assert.Equal(expected==200?1:0,p.Saves);
  if(expected==200){p.Granted=false;using var denied=await client.PutAsJsonAsync(path,new UpdateBranchPromptPayQrV1(1,p.Asset,true));Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);Assert.Equal(1,p.Saves);}
 }
 [Theory][InlineData(null,401)][InlineData("cashier",403)][InlineData("owner",403)][InlineData("other-workload",403)][InlineData("nexaconnect-media-service",200)]
 public async Task Owner_reference_validation_is_media_workload_only(string? identity,int expected)
 {
  var p=new Ports();await using var app=await Host(p);using var client=app.GetTestClient();Identify(client,identity);
  using var response=await client.GetAsync($"/api/restaurant/v1/internal/organizations/{p.Org}/branches/{p.Branch}/promptpay-owner");Assert.Equal(expected,(int)response.StatusCode);
 }
 [Fact]public async Task Invalid_unapproved_or_unready_selection_never_writes()
 {
  var p=new Ports();await using var app=await Host(p);using var client=app.GetTestClient();Identify(client,"owner");var path=$"/api/restaurant/v1/customer/organizations/{p.Org}/branches/{p.Branch}/promptpay-qr";
  using var unapproved=await client.PutAsJsonAsync(path,new UpdateBranchPromptPayQrV1(1,p.Asset,false));Assert.Equal(HttpStatusCode.BadRequest,unapproved.StatusCode);
  p.Ready=false;using var unready=await client.PutAsJsonAsync(path,new UpdateBranchPromptPayQrV1(1,p.Asset,true));Assert.Equal(HttpStatusCode.BadRequest,unready.StatusCode);Assert.Equal(0,p.Saves);
  using var wrong=await client.PutAsJsonAsync(path.Replace(p.Org.ToString(),Guid.NewGuid().ToString()),new UpdateBranchPromptPayQrV1(1,p.Asset,true));Assert.Equal(HttpStatusCode.NotFound,wrong.StatusCode);
 }
 private static void Identify(HttpClient client,string? value){if(value is not null)client.DefaultRequestHeaders.Add("X-Test-Identity",value);}
 private static async Task<WebApplication> Host(Ports p)
 {
  var builder=WebApplication.CreateBuilder();builder.WebHost.UseTestServer();builder.Services.AddLogging(l=>l.ClearProviders());
  builder.Services.AddAuthentication("QrTest").AddScheme<AuthenticationSchemeOptions,Identity>("QrTest",_=>{});
  builder.Services.AddAuthorization(o=>o.AddPolicy(NexaAuthorizationPolicies.GalleryMediaWorkload,r=>r.RequireAuthenticatedUser().RequireClaim("azp","nexaconnect-media-service")));
  builder.Services.AddControllers().AddApplicationPart(typeof(RESTAURANT::NexaConnect.Services.Restaurant.Controllers.BranchPromptPayQrController).Assembly);
  builder.Services.AddSingleton<IBranchPromptPayQrRepository>(p);builder.Services.AddSingleton<IBranchCustomerAuthorizer>(p);builder.Services.AddSingleton<IPromptPayQrAssetValidator>(p);builder.Services.AddScoped<BranchPromptPayQrService>();
  var app=builder.Build();app.UseAuthentication();app.UseAuthorization();app.MapControllers();await app.StartAsync();return app;
 }
 private sealed class Identity(IOptionsMonitor<AuthenticationSchemeOptions> options,ILoggerFactory logger,UrlEncoder encoder):AuthenticationHandler<AuthenticationSchemeOptions>(options,logger,encoder)
 {
  protected override Task<AuthenticateResult> HandleAuthenticateAsync(){string id=Request.Headers["X-Test-Identity"].ToString();if(id.Length==0)return Task.FromResult(AuthenticateResult.NoResult());return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub","actor"),new Claim("azp",id),new Claim(ClaimTypes.Role,id=="owner"?"customer-owner":"customer-user")],Scheme.Name)),Scheme.Name)));}
 }
 private sealed class Ports:IBranchPromptPayQrRepository,IBranchCustomerAuthorizer,IPromptPayQrAssetValidator
 {
  public Guid Org=Guid.NewGuid(),Restaurant=Guid.NewGuid(),Branch=Guid.NewGuid(),Asset=Guid.NewGuid();public bool Granted=true,Ready=true,ConfigGranted=true;public int Saves;
  public Task<BranchPromptPayQrV1?> GetAsync(Guid org,Guid branch,CancellationToken ct)=>Task.FromResult<BranchPromptPayQrV1?>(org==Org&&branch==Branch?new(org,Restaurant,branch,1,null):null);
  public Task<BranchPromptPayQrV1?> SaveAsync(Guid org,Guid branch,PromptPayQrApproval approval,string actor,CancellationToken ct){Saves++;return Task.FromResult<BranchPromptPayQrV1?>(new(org,Restaurant,branch,2,approval.AssetId));}
  public Task<bool> IsGrantedAsync(Guid org,Guid? restaurant,Guid? branch,string permission,string bearer,CancellationToken ct)=>Task.FromResult(Granted&&(permission!="restaurant.configuration.manage"||ConfigGranted)&&org==Org&&branch==Branch&&restaurant==Restaurant);
  public Task<bool> ReadyAsync(Guid org,Guid branch,Guid asset,string bearer,CancellationToken ct)=>Task.FromResult(Ready&&org==Org&&branch==Branch&&asset==Asset);
 }
}
