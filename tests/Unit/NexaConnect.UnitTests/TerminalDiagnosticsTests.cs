using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using NexaConnect.POS;

namespace NexaConnect.UnitTests;

public sealed class TerminalDiagnosticsTests
{
    private static PosClientConfiguration Configuration()=>new("https://identity.test/realms/private-realm","nexaconnect-pos","nexaconnect-pos://oauth/callback","openid",
        "https://pos.test/private/","https://order.test/",Guid.NewGuid(),Guid.NewGuid(),"THB","cash_manual",null,Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),CatalogApi:"https://catalog.test/");
    private static TerminalSetupFacts Facts()=>new(false,PosDisplayDensity.Standard,ReceiptPaper.Standard,false,null,null,false,2,false,false,false,false);
    [Theory][InlineData(101,TerminalReadiness.Unavailable)][InlineData(200,TerminalReadiness.Detected)][InlineData(401,TerminalReadiness.Detected)][InlineData(404,TerminalReadiness.Detected)][InlineData(302,TerminalReadiness.Unavailable)][InlineData(503,TerminalReadiness.Unavailable)]
    public async Task Anonymous_header_only_checks_do_not_follow_redirects_or_claim_business_health(int status,TerminalReadiness expected)
    {
        var handler=new ProbeHandler(status);using var client=new PosTerminalDiagnostics(Configuration(),handler);
        var results=await client.CheckAsync(default);Assert.Equal(5,handler.Requests);
        Assert.All(results.Where(r=>r.Configured),r=>Assert.Equal(expected,r.Status));
        Assert.Equal(TerminalReadiness.Untested,results.Single(r=>r.Service==TerminalService.Media).Status);
        var rows=TerminalSetup.Rows(Facts(),results);Assert.Contains(rows,r=>r.Detail.Contains("Health, identity and business access remain unverified"));
    }
    [Fact]public async Task Transport_errors_and_cancelled_checks_never_surface_exception_text()
    {
        using var client=new PosTerminalDiagnostics(Configuration(),new ProbeHandler(200){Fail=true});
        var results=await client.CheckAsync(default);Assert.All(results.Where(r=>r.Configured),r=>{Assert.Equal(TerminalReadiness.Unavailable,r.Status);Assert.Null(r.HttpStatus);});
        using var cancellation=new CancellationTokenSource();cancellation.Cancel();
        Assert.All((await client.CheckAsync(cancellation.Token)).Where(r=>r.Configured),r=>Assert.Equal(TerminalReadiness.Unavailable,r.Status));
    }
    [Fact]public void Setup_detects_settings_and_windows_presence_without_marking_hardware_accepted()
    {
        var rows=TerminalSetup.Rows(Facts() with{HasSavedPrinter=true,PrinterCount=1,SavedPrinterDetected=true,CustomerDisplayActive=true,ScannerMode=true},[]);
        Assert.Equal(TerminalReadiness.Detected,rows.Single(r=>r.Area=="Printer").Status);
        Assert.Equal(TerminalReadiness.Configured,rows.Single(r=>r.Area=="Customer view").Status);
        Assert.Equal(TerminalReadiness.Untested,rows.Single(r=>r.Area=="Scanner device").Status);
        Assert.Equal(TerminalReadiness.Untested,rows.Single(r=>r.Area=="Output acceptance").Status);
        Assert.Equal(TerminalReadiness.Unavailable,TerminalSetup.Rows(Facts() with{HasSavedPrinter=true,SavedPrinterDetected=false},[]).Single(r=>r.Area=="Printer").Status);
        Assert.Equal(TerminalReadiness.Unavailable,TerminalSetup.Rows(Facts() with{DisplaySafetyBlocked=true},[]).Single(r=>r.Area=="Displays").Status);
        Assert.Equal(TerminalReadiness.Unavailable,TerminalSetup.Rows(Facts() with{PrinterCount=0},[]).Single(r=>r.Area=="Printer").Status);
    }
    [Fact]public void Export_has_an_explicit_whitelist_without_raw_configuration_or_ui_text()
    {
        var c=Configuration();using var client=new PosTerminalDiagnostics(c,new ProbeHandler(200));
        var report=TerminalSupportReport.Create(Facts(),client.Initial(),new(1,2,3),new(10,0),new(10,0,26100));
        byte[] bytes=report.Serialize();using var doc=JsonDocument.Parse(bytes);
        var allowed=new HashSet<string>(["Version","GeneratedAtUtc","ApplicationVersion","RuntimeVersion","WindowsVersion","TouchFirst","Density","Paper","HasSavedPrinter","PrinterCount","SavedPrinterDetected","PrinterDiscoveryFailed","DisplayCount","DisplayDiscoveryFailed","CustomerDisplayActive","DisplaySafetyBlocked","ScannerMode","Services","PrinterCheckedAtUtc","DisplayCheckedAtUtc"]);
        Assert.True(allowed.SetEquals(doc.RootElement.EnumerateObject().Select(p=>p.Name)));
        string json=System.Text.Encoding.UTF8.GetString(bytes);
        foreach(string excluded in new[]{c.Authority,c.PosApi,c.OrganizationId.ToString(),c.BranchId.ToString(),c.TerminalId.ToString(),"Authorization","Cookie","Barcode","PrinterName","Subject"})Assert.DoesNotContain(excluded,json);
        Assert.InRange(bytes.Length,1,16384);
    }
    [Theory][InlineData("version")][InlineData("count")][InlineData("service")][InlineData("status")]
    public void Invalid_or_injectable_report_fields_fail_closed(string change)
    {
        using var client=new PosTerminalDiagnostics(Configuration(),new ProbeHandler(200));
        var report=TerminalSupportReport.Create(Facts(),client.Initial(),new(1,0),new(10,0),new(10,0));
        if(change=="version")report=report with{ApplicationVersion="secret.example/token"};
        if(change=="count")report=report with{PrinterCount=999};
        if(change=="service")report=report with{Services=[report.Services[0]]};
        if(change=="status")report.Services[0]=report.Services[0] with{Status=(TerminalReadiness)999};
        Assert.Throws<InvalidDataException>(()=>report.Serialize());
    }
    [Fact]public void Export_is_atomic_and_cleans_up_after_a_refused_destination()
    {
        string root=Path.Combine(Path.GetTempPath(),"Nexa-SupportReport",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try{
            using var client=new PosTerminalDiagnostics(Configuration(),new ProbeHandler(200));
            var report=TerminalSupportReport.Create(Facts(),client.Initial(),new(1,0),new(10,0),new(10,0));
            string file=Path.Combine(root,"report.json");report.Save(file);Assert.Equal(report.Serialize(),File.ReadAllBytes(file));
            // A directory cannot be atomically replaced by a report on any supported platform.
            string blocked=Path.Combine(root,"blocked.json");Directory.CreateDirectory(blocked);
            var error=Record.Exception(()=>report.Save(blocked));Assert.True(error is IOException or UnauthorizedAccessException);
            Assert.Empty(Directory.GetFiles(root,"*.tmp"));Assert.Equal(report.Serialize(),File.ReadAllBytes(file));
        }finally{Directory.Delete(root,true);}
    }
    private sealed class ProbeHandler(int status):HttpMessageHandler
    {
        public int Requests;public bool Fail;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            Interlocked.Increment(ref Requests);Assert.Equal(HttpMethod.Get,request.Method);Assert.Null(request.Content);Assert.Null(request.Headers.Authorization);
            Assert.False(request.Headers.Contains("Cookie"));Assert.False(request.Headers.Contains("X-Nexa-Organization-Id"));Assert.Equal("",request.RequestUri!.Query);
            Assert.True(request.RequestUri.AbsolutePath=="/"||request.RequestUri.AbsolutePath=="/realms/private-realm/.well-known/openid-configuration");
            Assert.True(Guid.TryParse(Assert.Single(request.Headers.GetValues("X-Correlation-ID")),out _));
            ct.ThrowIfCancellationRequested();if(Fail)throw new HttpRequestException("secret endpoint and token must never be returned");
            var response=new HttpResponseMessage((HttpStatusCode)status){Content=new NeverReadContent()};
            response.Headers.Location=new Uri("https://must-not-follow.test/secret");return Task.FromResult(response);
        }
    }
    private sealed class NeverReadContent:HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream,TransportContext? context)=>throw new InvalidOperationException("Diagnostics must never read response bodies.");
        protected override bool TryComputeLength(out long length){length=0;return false;}
    }
}
