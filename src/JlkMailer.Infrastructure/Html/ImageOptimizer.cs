using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Processing;

namespace JlkMailer.Infrastructure.Html;

public sealed record OptimizedImage(byte[] Bytes, string MediaType, string Extension, int Width, int Height);

/// <summary>
/// 설계 §09 2단계. base64 인라인 843KB → CID 첨부 180KB 이하.
/// </summary>
public static class ImageOptimizer
{
    public static OptimizedImage Optimize(byte[] source, EmailBuildOptions options)
    {
        using var image = Image.Load(source);

        if (image.Width > options.MaxImageWidth)
        {
            var ratio = (double)options.MaxImageWidth / image.Width;
            var height = Math.Max(1, (int)Math.Round(image.Height * ratio));
            image.Mutate(x => x.Resize(options.MaxImageWidth, height));
        }

        using var ms = new MemoryStream();
        if (options.KeepPng)
        {
            image.Save(ms, new PngEncoder { CompressionLevel = PngCompressionLevel.BestCompression });
            return new OptimizedImage(ms.ToArray(), "image/png", "png", image.Width, image.Height);
        }

        image.Save(ms, new JpegEncoder { Quality = options.JpegQuality });
        return new OptimizedImage(ms.ToArray(), "image/jpeg", "jpg", image.Width, image.Height);
    }
}
