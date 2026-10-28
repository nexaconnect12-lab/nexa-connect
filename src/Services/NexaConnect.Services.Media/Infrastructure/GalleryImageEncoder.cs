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
        using var source=SKImage.FromBitmap(bitmap);
        int width=bitmap.Width,height=bitmap.Height;
        // Processed WebP can expand past the POS PNG cap. Reduce the delivery copy,
        // keeping original assets, decoded bounds and the per-image cap unchanged.
        for(int attempt=0;attempt<3;attempt++)
        {
            using var scaled=new SKBitmap(width,height,SKColorType.Rgba8888,SKAlphaType.Premul);
            using(var canvas=new SKCanvas(scaled))
                canvas.DrawImage(source,new SKRect(0,0,width,height),new SKSamplingOptions(SKFilterMode.Linear));
            using var image=SKImage.FromBitmap(scaled);using var png=image.Encode(SKEncodedImageFormat.Png,100);
            if(png.Size<=2_097_152)return png.ToArray();
            width=Math.Max(1,width*3/4);height=Math.Max(1,height*3/4);
        }
        throw new InvalidDataException();
    }
}
