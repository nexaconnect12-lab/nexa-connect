using System.Security.Cryptography;
using System.Text;
namespace NexaConnect.POS.Hardware.Drawers;

public interface IDrawerHistoryProtector{byte[] Protect(byte[] bytes);byte[] Unprotect(byte[] bytes);}
public sealed class WindowsDrawerHistoryProtector:IDrawerHistoryProtector
{public byte[] Protect(byte[] bytes)=>WindowsDataProtection.Protect(bytes);public byte[] Unprotect(byte[] bytes)=>WindowsDataProtection.Unprotect(bytes);}
public sealed record DrawerLocalHistoryEntry(DrawerRequest Request,DateTimeOffset ReservedAtUtc,string LocalStatus);
public interface ILocalDrawerHistory
{
    IReadOnlyList<DrawerLocalHistoryEntry> ReadHistory(PosClientConfiguration configuration,string subject,DateTimeOffset from,DateTimeOffset to,int limit=50);
}
internal sealed record DrawerProtectedContext(int Version,string Authority,DrawerRequest Request,DateTimeOffset ReservedAtUtc);
internal static class DrawerHistoryIdentity
{
    public static string Hash(string authority,Guid org,Guid restaurant,Guid branch,Guid store,Guid terminal,string subject)=>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{authority}|{org:N}|{restaurant:N}|{branch:N}|{store:N}|{terminal:N}|{subject}")));
}
