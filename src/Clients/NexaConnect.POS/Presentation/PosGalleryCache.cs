using System.IO;
using System.Text.Json;
using NexaConnect.Contracts.Media;

namespace NexaConnect.POS;

internal sealed class PosGalleryCache(Guid org,Guid branch,string? root=null)
{
    private readonly string path=Path.Combine(root??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"NexaConnect","GalleryCache"),"screensaver-gallery-v1.bin");
    public GalleryBundle? Load(DateTimeOffset now)
    {
        try {
            if(!File.Exists(path)||new FileInfo(path).Length>25_165_824)return null;
            var value=JsonSerializer.Deserialize<GalleryBundle>(WindowsDataProtection.Unprotect(File.ReadAllBytes(path)));
            return value is not null&&PosGalleryPolicy.Valid(value,org,branch,now)?value:null;
        }catch(Exception e)when(e is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException or JsonException){return null;}
    }
    public void Save(GalleryBundle bundle,DateTimeOffset now)
    {
        if(!PosGalleryPolicy.Valid(bundle,org,branch,now))throw new InvalidDataException("Invalid gallery cache.");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary=path+".tmp";
        try{File.WriteAllBytes(temporary,WindowsDataProtection.Protect(JsonSerializer.SerializeToUtf8Bytes(bundle)));File.Move(temporary,path,true);}
        finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
    public void Clear(){if(File.Exists(path))File.Delete(path);}
}
