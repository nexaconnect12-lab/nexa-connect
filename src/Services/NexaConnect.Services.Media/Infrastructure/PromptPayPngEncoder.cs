using NexaConnect.Services.Media.Application;
using SkiaSharp;
namespace NexaConnect.Services.Media.Infrastructure;

public sealed class PromptPayPngEncoder:IPromptPayPngEncoder
{
    public byte[] Normalize(byte[] png)
    {
        if(png.Length is <8 or >2_097_152||!png.AsSpan(0,8).SequenceEqual(new byte[]{137,80,78,71,13,10,26,10}))throw new InvalidDataException();
        for(int offset=8;offset<png.Length;){
            if(png.Length-offset<12)throw new InvalidDataException();
            uint length=System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset,4));
            if(length>png.Length-offset-12)throw new InvalidDataException();
            var type=png.AsSpan(offset+4,4);
            if(type.SequenceEqual("acTL"u8)||type.SequenceEqual("fcTL"u8)||type.SequenceEqual("fdAT"u8))throw new InvalidDataException();
            offset+=12+(int)length;if(type.SequenceEqual("IEND"u8))break;
        }
        using var data=SKData.CreateCopy(png);using var codec=SKCodec.Create(data)??throw new InvalidDataException();
        if(codec.EncodedFormat!=SKEncodedImageFormat.Png||codec.FrameCount>1||codec.Info.Width is <64 or >2048||codec.Info.Height is <64 or >2048)throw new InvalidDataException();
        using var bitmap=new SKBitmap(codec.Info.Width,codec.Info.Height,SKColorType.Rgba8888,SKAlphaType.Premul);
        if(codec.GetPixels(bitmap.Info,bitmap.GetPixels())!=SKCodecResult.Success)throw new InvalidDataException();
        using var image=SKImage.FromBitmap(bitmap);using var encoded=image.Encode(SKEncodedImageFormat.Png,100);
        if(encoded.Size>2_097_152)throw new InvalidDataException();
        return encoded.ToArray(); // Original dimensions: no crop, rescale or lossy variant.
    }
}
