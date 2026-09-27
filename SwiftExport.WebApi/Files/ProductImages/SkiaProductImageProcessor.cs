using SkiaSharp;
using System.Buffers;
using System.Security.Cryptography;
using SwiftExport.Application.Interfaces.Files.ProductImages;
using SwiftExport.Contracts.DTOs.Files.ProductImages;

namespace SwiftExport.WebApi.Files.ProductImages;

/// <summary>使用 SkiaSharp 4.152.1 在受限并发下验证、方向修正并编码产品原图和缩略图。</summary>
public sealed class SkiaProductImageProcessor : IProductImageProcessor, IDisposable
{
    private readonly SemaphoreSlim _processingSlots;

    /// <summary>使用宿主配置的正并发数建立进程内原生解码限流器。</summary>
    public SkiaProductImageProcessor(int maxConcurrentProcessing)
    {
        if (maxConcurrentProcessing <= 0) throw new ArgumentOutOfRangeException(nameof(maxConcurrentProcessing));
        _processingSlots = new SemaphoreSlim(maxConcurrentProcessing, maxConcurrentProcessing);
    }

    /// <inheritdoc />
    public async Task<ProductImageProcessingResult> ProcessAsync(
        Stream source,
        ProductImageInputFormat expectedFormat,
        long maxFileBytes,
        long maxPixels,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!await _processingSlots.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            await DrainAsync(source, cancellationToken).ConfigureAwait(false);
            throw new ProductImageProcessingException(
                ProductImageErrorCodes.ProcessingBusy,
                "服务器图片处理容量已满，请稍后重试该文件。");
        }
        try
        {
            var sourceBytes = await ReadBoundedAsync(source, maxFileBytes, cancellationToken).ConfigureAwait(false);
            if (sourceBytes is null)
                throw new ProductImageProcessingException(
                    ProductImageErrorCodes.FileTooLarge,
                    "文件超过单张上传大小上限。");
            return await Task.Run(
                () => ProcessCore(sourceBytes, expectedFormat, maxPixels, cancellationToken),
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _processingSlots.Release();
        }
    }

    /// <summary>在取得处理容量后暂存单张压缩字节；超限仍排空当前文件节。</summary>
    private static async Task<byte[]?> ReadBoundedAsync(
        Stream source,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        using var destination = new MemoryStream((int)Math.Min(maxBytes, 1024 * 1024));
        var buffer = ArrayPool<byte>.Shared.Rent(81920);
        var tooLarge = false;
        long total = 0;
        try
        {
            int read;
            while ((read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)
                       .ConfigureAwait(false)) > 0)
            {
                var previous = total;
                total += read;
                if (total > maxBytes)
                {
                    tooLarge = true;
                    var allowed = (int)Math.Max(0, maxBytes - previous);
                    if (allowed > 0)
                        await destination.WriteAsync(buffer.AsMemory(0, allowed), cancellationToken).ConfigureAwait(false);
                    continue;
                }

                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            }

            return tooLarge ? null : destination.ToArray();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static async Task DrainAsync(Stream source, CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(81920);
        try
        {
            while (await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)
                       .ConfigureAwait(false) > 0)
            {
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>先读取编码头和帧信息，再按像素上限分配位图，避免损坏输入诱导超大分配。</summary>
    private static ProductImageProcessingResult ProcessCore(
        byte[] sourceBytes,
        ProductImageInputFormat expectedFormat,
        long maxPixels,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var input = new SKMemoryStream(sourceBytes);
        using var codec = SKCodec.Create(input) ?? throw InvalidImage();
        // Skia 对部分静态编码报告 0，对动画报告大于 1；只拒绝明确的多帧。
        if (codec.FrameCount > 1)
            throw new ProductImageProcessingException(
                ProductImageErrorCodes.MultipleFrames,
                "动画或多帧图片不受支持。");
        ValidateFormat(codec.EncodedFormat, expectedFormat);

        var info = codec.Info;
        if (info.Width <= 0 || info.Height <= 0 || (long)info.Width * info.Height > maxPixels)
            throw new ProductImageProcessingException(
                ProductImageErrorCodes.PixelLimitExceeded,
                "图片像素尺寸超过允许上限。");

        using var colorSpace = SKColorSpace.CreateSrgb();
        var decodeInfo = new SKImageInfo(
            info.Width,
            info.Height,
            SKColorType.Bgra8888,
            SKAlphaType.Premul,
            colorSpace);
        using var decoded = new SKBitmap(decodeInfo);
        var decodeResult = codec.GetPixels(decodeInfo, decoded.GetPixels());
        if (decodeResult != SKCodecResult.Success)
            throw InvalidImage();

        cancellationToken.ThrowIfCancellationRequested();
        using var oriented = RenderOrientedOnWhite(decoded, codec.EncodedOrigin);
        var originalBytes = EncodeJpeg(oriented, ProductImageEncodingSpecification.OriginalJpegQuality);
        using var thumbnail = RenderThumbnail(oriented);
        var thumbnailBytes = EncodeJpeg(thumbnail, ProductImageEncodingSpecification.ThumbnailJpegQuality);
        var sourceSha256 = SHA256.HashData(originalBytes);
        thumbnailBytes = JpegThumbnailSourceFingerprintCodec.Embed(thumbnailBytes, sourceSha256);
        cancellationToken.ThrowIfCancellationRequested();
        return new ProductImageProcessingResult
        {
            OriginalBytes = originalBytes,
            ThumbnailBytes = thumbnailBytes,
            OriginalWidth = oriented.Width,
            OriginalHeight = oriented.Height,
            ThumbnailWidth = thumbnail.Width,
            ThumbnailHeight = thumbnail.Height
        };
    }

    private static void ValidateFormat(SKEncodedImageFormat actual, ProductImageInputFormat expected)
    {
        var supported = actual is SKEncodedImageFormat.Jpeg or SKEncodedImageFormat.Png or SKEncodedImageFormat.Webp;
        if (!supported)
            throw new ProductImageProcessingException(
                ProductImageErrorCodes.UnsupportedType,
                "实际图片编码不是 JPEG、PNG 或 WebP。");

        var matches = expected switch
        {
            ProductImageInputFormat.Jpeg => actual == SKEncodedImageFormat.Jpeg,
            ProductImageInputFormat.Png => actual == SKEncodedImageFormat.Png,
            ProductImageInputFormat.Webp => actual == SKEncodedImageFormat.Webp,
            _ => false
        };
        if (!matches)
            throw new ProductImageProcessingException(
                ProductImageErrorCodes.TypeMismatch,
                "文件扩展名与实际图片编码不一致。");
    }

    /// <summary>按 EXIF 原点矩阵修正方向，并在白色背景上合成透明像素。</summary>
    private static SKBitmap RenderOrientedOnWhite(SKBitmap source, SKEncodedOrigin origin)
    {
        var swapsAxes = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop
            or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var width = swapsAxes ? source.Height : source.Width;
        var height = swapsAxes ? source.Width : source.Height;
        var result = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque));
        using var canvas = new SKCanvas(result);
        canvas.Clear(SKColors.White);
        canvas.SetMatrix(CreateOrientationMatrix(origin, source.Width, source.Height));
        canvas.DrawBitmap(source, 0, 0, new SKSamplingOptions(SKFilterMode.Linear));
        canvas.Flush();
        return result;
    }

    private static SKMatrix CreateOrientationMatrix(SKEncodedOrigin origin, int width, int height) => origin switch
    {
        SKEncodedOrigin.TopRight => new SKMatrix(-1, 0, width, 0, 1, 0, 0, 0, 1),
        SKEncodedOrigin.BottomRight => new SKMatrix(-1, 0, width, 0, -1, height, 0, 0, 1),
        SKEncodedOrigin.BottomLeft => new SKMatrix(1, 0, 0, 0, -1, height, 0, 0, 1),
        SKEncodedOrigin.LeftTop => new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1),
        SKEncodedOrigin.RightTop => new SKMatrix(0, -1, height, 1, 0, 0, 0, 0, 1),
        SKEncodedOrigin.RightBottom => new SKMatrix(0, -1, height, -1, 0, width, 0, 0, 1),
        SKEncodedOrigin.LeftBottom => new SKMatrix(0, 1, 0, -1, 0, width, 0, 0, 1),
        _ => SKMatrix.Identity
    };

    /// <summary>按最长边 256 等比缩小；小图保持原像素尺寸但仍独立编码。</summary>
    private static SKBitmap RenderThumbnail(SKBitmap source)
    {
        var scale = Math.Min(
            1d,
            ProductImageEncodingSpecification.ThumbnailMaxEdge /
            (double)Math.Max(source.Width, source.Height));
        var width = Math.Max(1, (int)Math.Round(source.Width * scale));
        var height = Math.Max(1, (int)Math.Round(source.Height * scale));
        var result = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque));
        using var canvas = new SKCanvas(result);
        canvas.Clear(SKColors.White);
        canvas.DrawBitmap(
            source,
            new SKRect(0, 0, width, height),
            new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        canvas.Flush();
        return result;
    }

    private static byte[] EncodeJpeg(SKBitmap bitmap, int quality)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, quality);
        if (data is null) throw InvalidImage();
        return data.ToArray();
    }

    private static ProductImageProcessingException InvalidImage() => new(
        ProductImageErrorCodes.ProcessingFailed,
        "图片内容损坏或无法完成解码。");

    /// <summary>释放处理并发信号量；正在执行的请求由宿主先完成再释放单例。</summary>
    public void Dispose() => _processingSlots.Dispose();
}
