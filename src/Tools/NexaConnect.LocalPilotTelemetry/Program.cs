using Microsoft.Extensions.Logging;
using NexaConnect.Observability;

// Only fixed operational enums/counters and a generated correlation may leave the host.
if(args.Length is not (4 or 5) || !PilotHostEvent.Valid(args[0],args[1],args[2],args[3]))return 2;
var options=new ObservabilityOptions();
if(args.Length==5)
{
    if(!Uri.TryCreate(args[4],UriKind.Absolute,out var endpoint)||!endpoint.IsLoopback||endpoint.Scheme is not ("http" or "https")||endpoint.UserInfo.Length!=0||endpoint.Query.Length!=0||endpoint.Fragment.Length!=0)return 2;
    options.OtlpEnabled=true;options.OtlpEndpoint=endpoint.AbsoluteUri;
}
try
{
    using var logs=NexaConnectObservabilityExtensions.CreateClientLoggerFactory("nexaconnect-local-pilot-host",options);
    using var correlation=CorrelationContext.Push(Guid.Parse(args[3]).ToString("D"));
    logs.CreateLogger("NexaConnect.LocalPilot.Host").LogInformation("Pilot host {Phase} {AttemptCount} {QrStatus} {CorrelationId}",args[0],int.Parse(args[1]),args[2],args[3]);
    return 0;
}
catch{return 1;}

public static class PilotHostEvent
{
    public static bool Valid(string phase,string attempts,string qr,string correlation)=>
        new[]{"starting","running","recovering","recovery-blocked","blocked-owner","blocked-budget","degraded-live-process","stop-waiting-pos","stop-blocked","stopped"}.Contains(phase)
        &&int.TryParse(attempts,out int count)&&count is >=0 and <=3
        &&new[]{"not-configured","running","stopped","unresponsive","changed-owner","recovering","blocked-refresh-required"}.Contains(qr)
        &&Guid.TryParseExact(correlation,"D",out var id)&&id!=Guid.Empty;
}
