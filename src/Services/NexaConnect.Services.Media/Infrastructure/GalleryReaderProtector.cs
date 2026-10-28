using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using NexaConnect.Services.Media.Application;

namespace NexaConnect.Services.Media.Infrastructure;

public sealed class GalleryReaderProtector(IDataProtectionProvider provider):IGalleryReaderProtector
{
    private readonly IDataProtector protector=provider.CreateProtector("nexaconnect.media.gallery-reader.v1");
    public string Protect(GalleryReaderGrant grant)=>protector.Protect(JsonSerializer.Serialize(grant));
    public GalleryReaderGrant? Read(string credential)
    {
        if(string.IsNullOrWhiteSpace(credential)||credential.Length>8192)return null;
        try{return JsonSerializer.Deserialize<GalleryReaderGrant>(protector.Unprotect(credential));}
        catch(Exception e)when(e is CryptographicException or JsonException or FormatException){return null;}
    }
}
