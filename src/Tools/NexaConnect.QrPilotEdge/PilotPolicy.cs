using System.Net;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;

namespace NexaConnect.QrPilotEdge;

public sealed record PilotOptions(string PublicOrigin,string ListenAddress,string BackendOrigin,string BackendCertificateSha256,
    string CertificatePath,string PrivateKeyPath)
{
    public void Validate()
    {
        if(!Origin(PublicOrigin,out var front)||!Origin(BackendOrigin,out var back)||!back!.IsLoopback
            || back.Host!="localhost" || front!.IsLoopback || !PrivateAddress(ListenAddress)
            || !Regex.IsMatch(BackendCertificateSha256,"\\A[0-9a-fA-F]{64}\\z")
            || string.IsNullOrWhiteSpace(CertificatePath)||string.IsNullOrWhiteSpace(PrivateKeyPath))
            throw new InvalidOperationException("QR pilot requires an exact HTTPS origin, private IPv4 listener and pinned localhost HTTPS backend.");
    }
    public static bool Origin(string? value,out Uri? uri)=>Uri.TryCreate(value,UriKind.Absolute,out uri)&&value.Length<=256
        &&uri.Scheme=="https"&&uri.AbsolutePath=="/"&&uri.UserInfo==""&&uri.Query==""&&uri.Fragment==""
        &&uri.HostNameType is UriHostNameType.Dns or UriHostNameType.IPv4&&uri.Port>=1024&&uri.GetLeftPart(UriPartial.Authority)==value;
    public static bool PrivateAddress(string value)=>IPAddress.TryParse(value,out var address)&&address.AddressFamily==System.Net.Sockets.AddressFamily.InterNetwork
        && address.GetAddressBytes() is var b && (b[0]==10||b[0]==172&&b[1] is >=16 and <=31||b[0]==192&&b[1]==168);
    public bool BackendCertificate(X509Certificate2? certificate,SslPolicyErrors errors)=>certificate is not null
        &&certificate.MatchesHostname(new Uri(BackendOrigin).Host,allowWildcards:false,allowCommonName:false)&&certificate.NotBefore.ToUniversalTime()<=DateTime.UtcNow
        &&certificate.NotAfter.ToUniversalTime()>DateTime.UtcNow
        &&CryptographicOperations.FixedTimeEquals(SHA256.HashData(certificate.RawData),Convert.FromHexString(BackendCertificateSha256));
    public static bool Allowed(string method,string target)
    {
        if(target.Length>200||target.Contains('%')||target.Contains('?')||target.Contains('\\'))return false;
        return method switch
        {
            "GET"=>target is "/order" or "/order/" or "/bff/guest/qr/context" or "/bff/guest/qr/menu" or "/bff/guest/qr/requests"
                || Regex.IsMatch(target,"\\A/assets/[A-Za-z0-9_-]+\\.(?:js|css|woff2|png|svg)\\z"),
            "POST"=>target is "/bff/guest/qr/sessions" or "/bff/guest/qr/quote" or "/bff/guest/qr/requests"
                || Regex.IsMatch(target,"\\A/bff/guest/qr/requests/[a-fA-F0-9]{8}-[a-fA-F0-9]{4}-[a-fA-F0-9]{4}-[a-fA-F0-9]{4}-[a-fA-F0-9]{12}/confirm-price\\z"),
            "DELETE"=>target=="/bff/guest/qr/sessions",
            _=>false
        };
    }
}
