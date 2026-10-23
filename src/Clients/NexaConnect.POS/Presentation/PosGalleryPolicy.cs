using System.Buffers.Binary;
using System.Security.Cryptography;
using NexaConnect.Contracts.Media;

namespace NexaConnect.POS;

public static class PosGalleryPolicy
{
    public static bool Valid(GalleryBundle bundle,Guid org,Guid branch,DateTimeOffset now)
    {
        var s=bundle.Selection;
        if(s is null||s.OrganizationId!=org||s.BranchId!=branch||s.Version<1||s.Staff is null||s.Customer is null||bundle.Images is null
            ||s.Staff.Length>6||s.Customer.Length>6||bundle.Images.Length>12||s.SecondsPerSlide is <5 or >60
            ||bundle.IssuedAtUtc>now.AddSeconds(30)||bundle.ExpiresAtUtc<=now||bundle.ExpiresAtUtc>now.AddMinutes(5)||bundle.ExpiresAtUtc<=bundle.IssuedAtUtc
            ||bundle.ExpiresAtUtc-bundle.IssuedAtUtc>TimeSpan.FromMinutes(5))return false;
        var slides=s.Staff.Concat(s.Customer).ToArray();
        if(slides.Any(x=>x is null||x.AssetId==Guid.Empty||x.Title is null||x.Caption is null||x.Title.Length>80||x.Caption.Length>160
            ||x.Title.Any(char.IsControl)||x.Caption.Any(char.IsControl))||s.Staff.Select(x=>x.AssetId).Distinct().Count()!=s.Staff.Length
            ||s.Customer.Select(x=>x.AssetId).Distinct().Count()!=s.Customer.Length)return false;
        if(bundle.Images.Any(x=>x is null||x.Png is null)||bundle.Images.Sum(x=>(long)x.Png.Length)>16_777_216
            ||bundle.Images.Select(x=>x.AssetId).Distinct().Count()!=bundle.Images.Length)return false;
        foreach(var image in bundle.Images)
        {
            var p=image.Png;
            if(p.Length is <24 or >2_097_152||!p.AsSpan(0,8).SequenceEqual(new byte[]{137,80,78,71,13,10,26,10})
                ||!p.AsSpan(12,4).SequenceEqual("IHDR"u8)||BinaryPrimitives.ReadUInt32BigEndian(p.AsSpan(16,4)) is <1 or >1280
                ||BinaryPrimitives.ReadUInt32BigEndian(p.AsSpan(20,4)) is <1 or >1280
                ||!string.Equals(Convert.ToHexString(SHA256.HashData(p)),image.Sha256,StringComparison.Ordinal))return false;
        }
        return slides.Select(x=>x.AssetId).ToHashSet().SetEquals(bundle.Images.Select(x=>x.AssetId));
    }
    public static GallerySlide? Slide(GalleryBundle? bundle,bool customer,DateTimeOffset now)
    {
        if(bundle is null||bundle.ExpiresAtUtc<=now)return null;
        var list=customer?bundle.Selection.Customer:bundle.Selection.Staff;
        if(list.Length==0)return null;
        var seconds=Math.Max(0,(now-bundle.IssuedAtUtc).TotalSeconds);
        return list[(int)(seconds/bundle.Selection.SecondsPerSlide)%list.Length];
    }
}
