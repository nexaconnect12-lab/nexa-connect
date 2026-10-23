using NexaConnect.Services.Media.Application;
using SkiaSharp;

namespace NexaConnect.Services.Media.Infrastructure;

public sealed class GalleryImageEncoder:IGalleryImageEncoder
{
    public byte[] ToPng(byte[] displayVariant)
    {
        if(displayVariant.Length is <1 or >2_097_152)throw new InvalidDataException();
        using var data=SKData.CreateCopy(displayVariant);using var codec=SKCodec.Create(data)??throw new InvalidDataException();
        if(codec.Info.Width is <1 or >1280||codec.Info.Height is <1 or >1280)throw new InvalidDataException();
        using var bitmap=new SKBitmap(codec.Info.Width,codec.Info.Height,SKColorType.Rgba8888,SKAlphaType.Premul);
        if(codec.GetPixels(bitmap.Info,bitmap.GetPixels())!=SKCodecResult.Success)throw new InvalidDataException();
        using var image=SKImage.FromBitmap(bitmap);using var png=image.Encode(SKEncodedImageFormat.Png,100);
        if(png.Size>2_097_152)throw new InvalidDataException();return png.ToArray();
    }
}
