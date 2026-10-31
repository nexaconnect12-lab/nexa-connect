using NexaConnect.Services.Media.Application;
using SkiaSharp;
namespace NexaConnect.Services.Media.Infrastructure;
public sealed class MenuPhotoEncoder:IMenuPhotoEncoder
{
    public byte[] Encode(byte[] source)
    {
        if(source.Length is <1 or >2_097_152)throw new InvalidDataException();
        using var data=SKData.CreateCopy(source);using var codec=SKCodec.Create(data)??throw new InvalidDataException();
        if(codec.Info.Width is <1 or >1280||codec.Info.Height is <1 or >1280)throw new InvalidDataException();
        using var bitmap=new SKBitmap(codec.Info.Width,codec.Info.Height,SKColorType.Rgba8888,SKAlphaType.Premul);
        if(codec.GetPixels(bitmap.Info,bitmap.GetPixels())!=SKCodecResult.Success)throw new InvalidDataException();
        using var image=SKImage.FromBitmap(bitmap);
        double factor=Math.Min(1,512d/Math.Max(bitmap.Width,bitmap.Height));
        int width=Math.Max(1,(int)(bitmap.Width*factor)),height=Math.Max(1,(int)(bitmap.Height*factor));
        for(int attempt=0;attempt<3;attempt++){
            using var resized=new SKBitmap(width,height,SKColorType.Rgba8888,SKAlphaType.Premul);
            using(var canvas=new SKCanvas(resized))canvas.DrawImage(image,new SKRect(0,0,width,height),new SKSamplingOptions(SKFilterMode.Linear));
            using var safe=SKImage.FromBitmap(resized);using var png=safe.Encode(SKEncodedImageFormat.Png,100);
            if(png.Size<=524288)return png.ToArray();width=Math.Max(1,width*3/4);height=Math.Max(1,height*3/4);
        }
        throw new InvalidDataException();
    }
}
