extern alias PAYMENT;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using PaymentProgram = PAYMENT::PaymentProgram;
using Inbox = PAYMENT::NexaConnect.Services.Payment.Application.Webhooks.IOmiseWebhookInbox;
using Claim = PAYMENT::NexaConnect.Services.Payment.Application.Webhooks.WebhookClaim;
using Ingress = PAYMENT::NexaConnect.Services.Payment.Application.Webhooks.OmiseWebhookIngress;

namespace NexaConnect.IntegrationTests;

public sealed class OmiseWebhookHttpTests
{
    private static readonly byte[] Key=Enumerable.Range(1,32).Select(i=>(byte)i).ToArray();
    private const string EventId="evnt_test_abcdefghijklmnopqrstuvwxyz";
    private sealed class FakeInbox : Inbox
    {
        public HashSet<string> Events = [];
        public bool Fail;
        public string? Correlation;
        public Task EnqueueAsync(string id,Guid correlation,CancellationToken token,string? traceCorrelationId=null) { if(Fail) throw new Npgsql.NpgsqlException("Fixture unavailable."); Events.Add(id); Correlation=traceCorrelationId; return Task.CompletedTask; }
        public Task<Claim?> ClaimAsync(TimeSpan lease,CancellationToken token)=>Task.FromResult<Claim?>(null);
        public Task FinishAsync(Claim claim,string outcome,TimeSpan delay,int attempts,CancellationToken token)=>Task.CompletedTask;
    }
    private sealed class Factory(bool enabled,FakeInbox inbox) : WebApplicationFactory<PaymentProgram>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing").ConfigureAppConfiguration((_,config)=>config.AddInMemoryCollection(new Dictionary<string,string?>
            {
                ["OmiseWebhooks:Enabled"]=enabled.ToString(), ["OmiseWebhooks:Secret"]=Convert.ToBase64String(Key),
                ["PaymentProvider:Adapter"]="Omise",["PaymentProvider:OmiseSecretKey"]="skey_test_abcdefghijklmnopqrstuvwxyz",
                ["PaymentProvider:LeaseDuration"]="00:01:10",["PaymentProvider:RequestTimeout"]="00:00:15",
                ["Persistence:Provider"]="PostgreSQL",["ConnectionStrings:Payment"]="Host=127.0.0.1;Database=unused;Username=unused;Password=unused",
                ["Outbox:Enabled"]="true",["Outbox:ConnectionString"]="amqp://guest:guest@127.0.0.1:5672/",
                ["Services:PlatformDirectory"]="https://localhost/",["Services:Restaurant"]="https://localhost/",
                ["Services:Order"]="https://localhost/",["Services:Authorization"]="https://localhost/",
                ["Authentication:Authority"]="https://localhost/",["Authentication:Audience"]="nexaconnect-api"
            }));
            builder.ConfigureTestServices(services=>
            {
                services.RemoveAll<IHostedService>(); services.RemoveAll<Inbox>(); services.AddSingleton<Inbox>(inbox);
                services.AddScoped<Ingress>();
            });
        }
    }
    [Theory]
    [InlineData("valid",HttpStatusCode.OK,1)] [InlineData("duplicate",HttpStatusCode.OK,1)]
    [InlineData("forged",HttpStatusCode.Unauthorized,0)] [InlineData("expired",HttpStatusCode.Unauthorized,0)]
    [InlineData("live",HttpStatusCode.BadRequest,0)] [InlineData("oversized",HttpStatusCode.RequestEntityTooLarge,0)]
    [InlineData("disabled",HttpStatusCode.NotFound,0)]
    [InlineData("persistence_failure",HttpStatusCode.ServiceUnavailable,0)]
    public async Task Http_ingress_requires_valid_signature_and_bounds_before_durable_ack(string kind,HttpStatusCode expected,int count)
    {
        var inbox=new FakeInbox {Fail=kind=="persistence_failure"}; using var factory=new Factory(kind!="disabled",inbox);
        using var client=factory.CreateClient(new WebApplicationFactoryClientOptions {BaseAddress=new Uri("https://localhost")});
        string body=kind=="oversized"?new string('x',65537):"{\"id\":\""+(kind=="live"?EventId.Replace("test","live"):EventId)+"\",\"data\":{\"paid\":true}}";
        string timestamp=(DateTimeOffset.UtcNow.ToUnixTimeSeconds()-(kind=="expired"?301:0)).ToString();
        string signature=Convert.ToHexStringLower(HMACSHA256.HashData(Key,Encoding.UTF8.GetBytes(timestamp+"."+body)));
        for(int i=0;i<(kind=="duplicate"?2:1);i++)
        {
            using var request=new HttpRequestMessage(HttpMethod.Post,"/api/payment/v1/webhooks/omise") {Content=new StringContent(body,Encoding.UTF8,"application/json")};
            request.Headers.Add("Omise-Signature",kind=="forged"?new string('0',64):signature);
            request.Headers.Add("Omise-Signature-Timestamp",timestamp);
            request.Headers.Add("X-Correlation-ID","webhook.test:123");
            using var response=await client.SendAsync(request);
            Assert.Equal(expected,response.StatusCode);
        }
        Assert.Equal(count,inbox.Events.Count);
        if(count>0) Assert.Equal("webhook.test:123",inbox.Correlation);
    }
    [Fact]
    public async Task Ingress_rate_limit_bounds_signed_duplicate_requests_without_queue_growth()
    {
        var inbox=new FakeInbox(); using var factory=new Factory(true,inbox);
        using var client=factory.CreateClient(new WebApplicationFactoryClientOptions {BaseAddress=new Uri("https://localhost")});
        string body="{\"id\":\""+EventId+"\"}";
        string timestamp=DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        string signature=Convert.ToHexStringLower(HMACSHA256.HashData(Key,Encoding.UTF8.GetBytes(timestamp+"."+body)));
        for(int i=0;i<61;i++)
        {
            using var request=new HttpRequestMessage(HttpMethod.Post,"/api/payment/v1/webhooks/omise") {Content=new StringContent(body,Encoding.UTF8,"application/json")};
            request.Headers.Add("Omise-Signature",signature);request.Headers.Add("Omise-Signature-Timestamp",timestamp);
            using var response=await client.SendAsync(request);
            Assert.Equal(i<60?HttpStatusCode.OK:HttpStatusCode.TooManyRequests,response.StatusCode);
        }
        Assert.Single(inbox.Events);
    }
}
