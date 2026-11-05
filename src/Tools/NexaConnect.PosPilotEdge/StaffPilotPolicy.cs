using System.Net;
using System.Text.RegularExpressions;

namespace NexaConnect.PosPilotEdge;

public sealed record StaffPilotOptions(string PublicOrigin,string ListenAddress,string DeviceAddress,string Realm,
    string CertificatePath,string PrivateKeyPath,Dictionary<string,string> Backends)
{
    public static readonly string[] ServiceNames=["pos","order","catalog","payment","media"];
    public static bool PrivateAddress(string? text)=>IPAddress.TryParse(text,out var ip)&&ip.ToString()==text&&ip.AddressFamily==System.Net.Sockets.AddressFamily.InterNetwork
        &&ip.GetAddressBytes() is var b&&(b[0]==10||b[0]==172&&b[1] is >=16 and <=31||b[0]==192&&b[1]==168);
    public void Validate()
    {
        if(!PrivateAddress(ListenAddress)||!PrivateAddress(DeviceAddress)||DeviceAddress==ListenAddress
            ||!Uri.TryCreate(PublicOrigin,UriKind.Absolute,out var front)||front.Scheme!="https"||front.Host!=ListenAddress
            ||front.Port<1024||front.GetLeftPart(UriPartial.Authority)!=PublicOrigin||front.UserInfo!=""||front.Query!=""||front.Fragment!=""
            ||!Regex.IsMatch(Realm,"\\Anexa-review-it-[a-f0-9]{32}\\z")||string.IsNullOrWhiteSpace(CertificatePath)||string.IsNullOrWhiteSpace(PrivateKeyPath)
            ||Backends.Count!=6||ServiceNames.Append("identity").Any(k=>!Backends.ContainsKey(k)))throw new InvalidOperationException("Exact isolated staff-pilot scope required.");
        foreach(var value in Backends.Values)
            if(!Uri.TryCreate(value,UriKind.Absolute,out var back)||back.Scheme!="http"||back.Host!="127.0.0.1"||back.Port<1024
                ||back.AbsolutePath!="/"||back.UserInfo!=""||back.Query!=""||back.Fragment!="")throw new InvalidOperationException("Staff upstreams must be owned IPv4 loopback HTTP origins.");
        if(Backends.Values.Distinct(StringComparer.Ordinal).Count()!=6)throw new InvalidOperationException("Distinct upstream origins required.");
    }
    public bool ClientAllowed(IPAddress? ip)=>ip?.MapToIPv4().ToString() is {} value&&(value==ListenAddress||value==DeviceAddress);
    public string? Route(string method,string target)
    {
        if(target.Length>8192||target.Contains('\\')||target.Contains('#')||target.Any(char.IsControl))return null;
        var path=target.Split('?')[0];
        if(path.Contains('%')||path.Split('/').Any(p=>p is "." or ".."))return null;
        if(method is "GET" or "POST"&&(
            path==$"/realms/{Realm}/.well-known/openid-configuration"
            ||Regex.IsMatch(path,$"\\A/realms/{Regex.Escape(Realm)}/protocol/openid-connect/(?:auth|token|userinfo|certs|logout)\\z")
            ||Regex.IsMatch(path,$"\\A/realms/{Regex.Escape(Realm)}/login-actions/(?:authenticate|restart|reset-credentials|required-action|action-token)\\z")
            ||method=="GET"&&Regex.IsMatch(path,"\\A/resources/[A-Za-z0-9_./-]+\\z")))return "identity";
        if(method is not ("GET" or "POST" or "PUT" or "DELETE" or "PATCH"))return null;
        foreach(var service in ServiceNames)
            if(Regex.IsMatch(path,$"\\A/api/{service}/v1/[A-Za-z0-9_/-]+\\z")
                &&!path.Split('/').Any(p=>p is "internal" or "guest" or "metrics"))return service;
        return null;
    }
}
