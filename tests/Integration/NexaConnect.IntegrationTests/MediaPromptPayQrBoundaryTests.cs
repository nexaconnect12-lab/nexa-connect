extern alias MEDIA;
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
using MEDIA::NexaConnect.Services.Media.Application;
namespace NexaConnect.IntegrationTests;

public sealed class MediaPromptPayQrBoundaryTests
{
 [Theory][InlineData(null,401)][InlineData("cashier",200)][InlineData("manager",200)]
 public async Task Signed_in_branch_reader_can_get_empty_selection_without_media_admin_grant(string? identity,int expected)
 {
  var port=new Selection();await using var app=await Host(port);using var client=app.GetTestClient();Identify(client,identity);
  using var response=await client.GetAsync(Path(port)+"/promptpay-qr");Assert.Equal(expected,(int)response.StatusCode);
  if(expected==200){Assert.False(port.LastManage);Assert.True(response.Headers.CacheControl?.NoStore);}
 }
 [Theory][InlineData(null,401)][InlineData("cashier",403)]
 public async Task Upload_completion_and_ready_checks_reject_before_storage_without_branch_management(string? identity,int expected)
 {
  var port=new Selection{Manage=false};await using var app=await Host(port);using var client=app.GetTestClient();Identify(client,identity);
  string path=Path(port);Guid asset=Guid.NewGuid();
  using var start=await client.PostAsJsonAsync(path+"/promptpay-uploads",new StartPromptPayQrUploadV1("fixture.png","image/png",100,new string('a',64)));
  using var complete=await client.PostAsJsonAsync(path+$"/promptpay-uploads/{asset}/complete",new CompletePromptPayQrUploadV1(1));
  using var ready=await client.GetAsync(path+$"/promptpay-assets/{asset}");
  using var preview=await client.GetAsync(path+"/promptpay-qr/management-preview");
  foreach(var response in new[]{start,complete,ready,preview})Assert.Equal(expected,(int)response.StatusCode);
  if(identity is not null)Assert.True(port.LastManage);
 }
 [Fact]public async Task Management_preview_requires_live_management_and_rechecks_before_delivery()
 {
  var port=new Selection();await using var app=await Host(port);using var client=app.GetTestClient();Identify(client,"manager");
  using var response=await client.GetAsync(Path(port)+"/promptpay-qr/management-preview");Assert.Equal(HttpStatusCode.OK,response.StatusCode);Assert.Equal(2,port.Reads);Assert.True(port.LastManage);
  port.Manage=false;using var denied=await client.GetAsync(Path(port)+"/promptpay-qr/management-preview");Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
 }
 private static string Path(Selection p)=>$"/api/media/v1/customer/organizations/{p.Org}/branches/{p.Branch}";
 private static void Identify(HttpClient c,string? identity){if(identity is not null)c.DefaultRequestHeaders.Add("X-Test-Identity",identity);}
 private static async Task<WebApplication> Host(Selection p)
 {
  var b=WebApplication.CreateBuilder();b.WebHost.UseTestServer();b.Services.AddLogging(l=>l.ClearProviders());
  b.Services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions,Identity>("Test",_=>{});b.Services.AddAuthorization();
  b.Services.AddControllers().AddApplicationPart(typeof(MEDIA::NexaConnect.Services.Media.Controllers.PromptPayQrController).Assembly);
  b.Services.AddSingleton(new PromptPayQrDelivery(p,null!,null!,null!,TimeProvider.System));b.Services.AddSingleton(new PromptPayQrUploads(p,null!,null!));
  var app=b.Build();app.UseAuthentication();app.UseAuthorization();app.MapControllers();await app.StartAsync();return app;
 }
 private sealed class Selection:IBranchPromptPayQrSelectionPort
 {
  public Guid Org=Guid.NewGuid(),Branch=Guid.NewGuid(),Restaurant=Guid.NewGuid();public bool Manage=true,LastManage;public int Reads;
  public Task<BranchPromptPayQrV1> ReadAsync(Guid org,Guid branch,string bearer,CancellationToken ct,bool manage=false){Reads++;LastManage=manage;if(org!=Org||branch!=Branch||manage&&!Manage)throw new UnauthorizedAccessException();return Task.FromResult(new BranchPromptPayQrV1(Org,Restaurant,Branch,1,null));}
 }
 private sealed class Identity(IOptionsMonitor<AuthenticationSchemeOptions> o,ILoggerFactory l,UrlEncoder e):AuthenticationHandler<AuthenticationSchemeOptions>(o,l,e)
 {
  protected override Task<AuthenticateResult> HandleAuthenticateAsync()=>Task.FromResult(Request.Headers.ContainsKey("X-Test-Identity")?AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub","fixture"),new Claim(ClaimTypes.Role,"customer-user")],Scheme.Name)),Scheme.Name)):AuthenticateResult.NoResult());
 }
}
