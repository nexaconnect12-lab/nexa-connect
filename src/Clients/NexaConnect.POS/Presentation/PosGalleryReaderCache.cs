using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NexaConnect.Contracts.Media;

namespace NexaConnect.POS;

internal sealed class PosGalleryReaderCache(PosClientConfiguration configuration,string? root=null)
{
    private sealed record Stored(string Subject,string Authority,GalleryReaderCredential Reader);
    private readonly string path=Path.Combine(root??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"NexaConnect","GalleryReaders"),
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{configuration.Authority}|{configuration.MediaApi}|{configuration.OrganizationId:N}|{configuration.RestaurantId:N}|{configuration.BranchId:N}|{configuration.StoreId:N}|{configuration.TerminalId:N}"))),"reader-v1.bin");
    public static bool Valid(GalleryReaderCredential? reader,Guid org,Guid branch,DateTimeOffset now)=>reader is not null
        && org!=Guid.Empty && branch!=Guid.Empty && reader.OrganizationId==org && reader.BranchId==branch
        && !string.IsNullOrWhiteSpace(reader.Credential) && reader.Credential.Length<=8192
        && !reader.Credential.Any(char.IsControl) && reader.IssuedAtUtc<=now.AddSeconds(30)
        && reader.ExpiresAtUtc>now && reader.ExpiresAtUtc>reader.IssuedAtUtc
        && reader.ExpiresAtUtc-reader.IssuedAtUtc<=TimeSpan.FromHours(24);
    public GalleryReaderCredential? Load(string subject,DateTimeOffset now)
    {
        try{
            PosInstallationPolicy.AssertNoLinks(path);
            if(!File.Exists(path))return null;
            using var stream=File.OpenRead(path);
            byte[] bytes=new byte[16385];int length=0;
            while(length<bytes.Length){int count=stream.Read(bytes.AsSpan(length));if(count==0)break;length+=count;}
            if(length is <=0 or >16384)return null;
            var stored=JsonSerializer.Deserialize<Stored>(WindowsDataProtection.Unprotect(bytes.AsSpan(0,length).ToArray()));
            return stored is not null && stored.Subject==subject && stored.Authority==configuration.Authority
                && Valid(stored.Reader,configuration.OrganizationId,configuration.BranchId,now)?stored.Reader:null;
        }catch(Exception e)when(e is IOException or UnauthorizedAccessException or CryptographicException or JsonException or InvalidDataException){return null;}
    }
    public void Save(GalleryReaderCredential reader,string subject,DateTimeOffset now)
    {
        if(string.IsNullOrWhiteSpace(subject)||!Valid(reader,configuration.OrganizationId,configuration.BranchId,now))throw new InvalidDataException();
        PosInstallationPolicy.AssertNoLinks(path);Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{
            var bytes=WindowsDataProtection.Protect(JsonSerializer.SerializeToUtf8Bytes(new Stored(subject,configuration.Authority,reader)));
            if(bytes.Length>16384)throw new InvalidDataException("Gallery reader cache exceeds its bound.");
            File.WriteAllBytes(temporary,bytes);
            PosInstallationPolicy.AssertNoLinks(path);File.Move(temporary,path,true);
        }finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
    public void Clear(){PosInstallationPolicy.AssertNoLinks(path);if(File.Exists(path))File.Delete(path);}
}
