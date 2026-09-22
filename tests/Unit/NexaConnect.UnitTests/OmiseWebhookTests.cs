using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using NexaConnect.Services.Payment.Application.Intents;
using NexaConnect.Services.Payment.Application.Webhooks;
using NexaConnect.Services.Payment.Infrastructure;
using NexaConnect.Services.Payment.Infrastructure.Providers;
using NexaConnect.Services.Payment.Infrastructure.Webhooks;

namespace NexaConnect.UnitTests;

public sealed class OmiseWebhookTests
{
    private const string EventId = "evnt_test_abcdefghijklmnopqrstuvwxyz";
    private const string ChargeId = "chrg_test_abcdefghijklmnopqrstuvwxyz";
    private static readonly byte[] Key = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();
    private static string Secret => Convert.ToBase64String(Key);
    private static string Sign(byte[] body, string timestamp) => Convert.ToHexStringLower(
        HMACSHA256.HashData(Key, Encoding.UTF8.GetBytes(timestamp + ".").Concat(body).ToArray()));

    [Fact]
    public async Task Testing_boundary_pauses_after_safe_marker_until_explicit_control()
    {
        string directory = Path.Combine(Path.GetTempPath(), "nexa-omise-boundary-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string marker = Path.Combine(directory, "marker.json"), control = Path.Combine(directory, "control.json");
        try
        {
            var boundary = new FileOmiseWebhookProcessingBoundary(Options.Create(new OmiseWebhookAcceptanceOptions
            { Enabled = true, MarkerPath = marker, ControlPath = control }));
            await boundary.AfterProcessingAsync("retry", false, default);
            Assert.False(File.Exists(marker));
            await boundary.AfterProcessingAsync("completed", false, default);
            Assert.False(File.Exists(marker));
            Task paused = boundary.AfterProcessingAsync("completed", true, default);
            for (int attempt = 0; attempt < 50 && !File.Exists(marker); attempt++) await Task.Delay(20);
            Assert.True(File.Exists(marker)); Assert.False(paused.IsCompleted);
            string text = await File.ReadAllTextAsync(marker);
            Assert.Contains("financial_committed_before_inbox_ack", text);
            Assert.DoesNotContain("evnt_", text); Assert.DoesNotContain("chrg_", text);
            await File.WriteAllTextAsync(control, "{\"phase\":\"continue\"}");
            await paused.WaitAsync(TimeSpan.FromSeconds(2));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData("valid", true)] [InlineData("modified", false)] [InlineData("old", false)]
    [InlineData("future", false)] [InlineData("malformed", false)] [InlineData("rotation", true)]
    [InlineData("wrong_key", false)] [InlineData("three_signatures", false)]
    public void Signature_checks_raw_body_time_key_and_rotation(string kind, bool expected)
    {
        DateTimeOffset now = DateTimeOffset.FromUnixTimeSeconds(1800000000);
        byte[] body = Encoding.UTF8.GetBytes("{\"id\":\"" + EventId + "\"}");
        string timestamp = (now.ToUnixTimeSeconds() + (kind == "old" ? -301 : kind == "future" ? 301 : 0)).ToString();
        string signature = Sign(body, timestamp);
        if (kind == "modified") body = Encoding.UTF8.GetBytes("{}");
        if (kind == "malformed") signature = new string('z', 64);
        if (kind == "rotation") signature = new string('0', 64) + "," + signature;
        if (kind == "three_signatures") signature = signature + "," + signature + "," + signature;
        string secret = kind == "wrong_key" ? Convert.ToBase64String(new byte[32]) : Secret;
        Assert.Equal(expected, OmiseWebhookSignature.Verify(body, timestamp, signature, secret, now));
    }

    [Theory]
    [InlineData("evnt_live_abcdefghijklmnopqrstuvwxyz")]
    [InlineData("evnt_test_abcdefghijklmnopqrstuvwxyz\n")]
    [InlineData("../events/foo")]
    public void Ingress_rejects_live_and_path_or_whitespace_ids(string id) =>
        Assert.Throws<ArgumentException>(() => OmiseWebhookIdentity.Parse(JsonSerializer.SerializeToUtf8Bytes(new { id })));

    private static object Event(PaymentIntent intent, string kind) => new
    {
        @object = "event", id = kind == "wrong_event" ? EventId + "x" : EventId,
        livemode = kind == "live", key = kind == "unsupported" ? "refund.create" : "charge.capture",
        data = new
        {
            @object = "charge", id = ChargeId, livemode = false,
            amount = kind == "fractional" ? (object)50.5m : 5000L,
            currency = kind == "null_currency" ? null : kind == "wrong_currency" ? "USD" : "thb",
            metadata = new Dictionary<string,string>
            {
                ["nexa_organization_id"] = kind == "empty_owner" ? Guid.Empty.ToString() : intent.OrganizationId.ToString(),
                ["nexa_intent_id"] = intent.Id.ToString(), ["nexa_order_id"] = intent.OrderId.ToString()
            }
        }
    };
    private sealed class Handler(HttpResponseMessage response) : HttpMessageHandler
    {
        public int Calls; public HttpMethod? Method; public Uri? Uri;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { Calls++; Method=request.Method; Uri=request.RequestUri; return Task.FromResult(response); }
    }
    [Theory]
    [InlineData("valid", true, false)] [InlineData("wrong_event", false, false)]
    [InlineData("live", false, false)] [InlineData("unsupported", false, false)]
    [InlineData("empty_owner", false, false)] [InlineData("fractional", false, false)]
    [InlineData("wrong_currency", false, false)] [InlineData("unavailable", false, true)]
    [InlineData("missing", false, false)] [InlineData("oversized", false, false)]
    [InlineData("null_currency", false, false)]
    public async Task Event_lookup_is_read_only_and_validates_canonical_identity(string kind, bool accepted, bool retry)
    {
        var intent = new PaymentIntent(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),50,"THB","card","capture_unknown",DateTimeOffset.UtcNow);
        var response = new HttpResponseMessage(kind == "unavailable" ? HttpStatusCode.ServiceUnavailable : kind == "missing" ? HttpStatusCode.NotFound : HttpStatusCode.OK)
        { Content = new StringContent(kind == "oversized" ? new string('x',65537) : JsonSerializer.Serialize(Event(intent,kind))) };
        var handler = new Handler(response);
        var verifier = new OmiseEventVerifier(new HttpClient(handler) { BaseAddress=new Uri("https://api.omise.co/") },
            Options.Create(new PaymentProviderOptions { OmiseSecretKey="skey_test_abcdefghijklmnopqrstuvwxyz" }));
        var result = await verifier.VerifyAsync(EventId,default);
        Assert.Equal(accepted,result.Event is not null); Assert.Equal(retry,result.Retry);
        Assert.Equal(1,handler.Calls); Assert.Equal(HttpMethod.Get,handler.Method);
        Assert.Equal("https://api.omise.co/events/"+EventId,handler.Uri!.AbsoluteUri);
    }
    private sealed class Lookup(OmiseEventLookup result) : IOmiseEventVerifier
    { public Task<OmiseEventLookup> VerifyAsync(string id,CancellationToken token)=>Task.FromResult(result); }
    private sealed class Recovery(bool completed) : IWebhookPaymentRecovery
    { public int Calls; public Task<bool> ReconcileAsync(PaymentIntent intent,PaymentMutationContext context,CancellationToken token) { Calls++; return Task.FromResult(completed); } }
    [Theory]
    [InlineData("match","completed",1)] [InlineData("busy","retry",1)]
    [InlineData("tenant","rejected",0)] [InlineData("amount","rejected",0)]
    [InlineData("order","rejected",0)] [InlineData("reference","rejected",0)]
    [InlineData("unverified","rejected",0)] [InlineData("transport","retry",0)]
    public async Task Processor_checks_local_ownership_and_amount_before_recovery(string kind,string expected,int calls)
    {
        var intents = new InMemoryPaymentIntents(); Guid org=Guid.NewGuid(); var context=new PaymentMutationContext("test",Guid.NewGuid());
        var intent = intents.Create(org,new(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),"webhook-test",50,"THB","card"),context);
        var lease=intents.BeginAuthorization(org,intent.Id,context);
        if (kind == "reference")
        {
            intent=intents.CompleteAuthorization(org,intent.Id,lease.Intent.ConcurrencyVersion,true,"another_reference",null,context);
            var capture=intents.BeginCapture(org,intent.Id,context);
            intent=intents.CompleteCapture(org,intent.Id,capture.Intent.ConcurrencyVersion,ProviderCaptureOutcome.Unknown,null,"provider_timeout",context);
        }
        else intent=intents.CompleteAuthorization(org,intent.Id,lease.Intent.ConcurrencyVersion,ProviderAuthorizationOutcome.Unknown,null,"provider_timeout",context);
        var message=new VerifiedOmiseEvent(kind=="tenant"?Guid.NewGuid():org,intent.Id,kind=="order"?Guid.NewGuid():intent.OrderId,ChargeId,kind=="amount"?5001:5000,"THB");
        var lookup=new Lookup(kind=="unverified"?new(null,false):kind=="transport"?new(null,true):new(message,false));
        var recovery=new Recovery(kind!="busy");
        var processor=new OmiseWebhookProcessor(lookup,intents,recovery);
        var result=await processor.ProcessAsync(new(EventId,Guid.NewGuid(),context.CorrelationId,1),default);
        Assert.Equal(expected,result.Outcome); Assert.Equal(expected=="completed"&&calls==1,result.FinancialTransitionCommitted);
        Assert.Equal(calls,recovery.Calls);
    }
    [Fact]
    public async Task Delayed_notifications_leave_captured_state_unchanged_without_payment_commands()
    {
        var intents=new InMemoryPaymentIntents(); Guid org=Guid.NewGuid(); var context=new PaymentMutationContext("test",Guid.NewGuid());
        var intent=intents.Create(org,new(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),"delayed",50,"THB","card"),context);
        var authorization=intents.BeginAuthorization(org,intent.Id,context);
        intent=intents.CompleteAuthorization(org,intent.Id,authorization.Intent.ConcurrencyVersion,true,ChargeId,null,context);
        var capture=intents.BeginCapture(org,intent.Id,context);
        intent=intents.CompleteCapture(org,intent.Id,capture.Intent.ConcurrencyVersion,ProviderCaptureOutcome.Captured,ChargeId,null,context);
        var recovery=new Recovery(true);
        var processor=new OmiseWebhookProcessor(new Lookup(new(new(org,intent.Id,intent.OrderId,ChargeId,5000,"THB"),false)),intents,recovery);
        for(int i=0;i<3;i++)
        {
            var result=await processor.ProcessAsync(new(EventId,Guid.NewGuid(),context.CorrelationId,1),default);
            Assert.Equal("completed",result.Outcome); Assert.False(result.FinancialTransitionCommitted);
        }
        Assert.Equal(0,recovery.Calls); Assert.Equal(intent.ConcurrencyVersion,intents.Get(org,intent.Id)!.ConcurrencyVersion);
    }
}
