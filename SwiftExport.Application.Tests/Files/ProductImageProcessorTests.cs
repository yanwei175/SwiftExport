using System.Security.Cryptography;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SkiaSharp;
using SwiftExport.Application.Interfaces.Files.ProductImages;
using SwiftExport.Contracts.DTOs.Files.ProductImages;
using SwiftExport.WebApi.Files.ProductImages;

namespace SwiftExport.Application.Tests.Files;

/// <summary>通过真实 SkiaSharp 原生编解码验证产品图片格式、方向、白底和尺寸规格。</summary>
[TestClass]
public sealed class ProductImageProcessorTests
{
    /// <summary>JPEG、PNG、WebP 均按实际内容转成 JPEG，长图缩到 256 且小图不放大。</summary>
    [TestMethod]
    public async Task SupportedFormats_ShouldEncodeJpegAndRespectThumbnailSize()
    {
        using var processor = new SkiaProductImageProcessor(2);
        foreach (var item in new[]
                 {
                     (SKEncodedImageFormat.Jpeg, ProductImageInputFormat.Jpeg),
                     (SKEncodedImageFormat.Png, ProductImageInputFormat.Png),
                     (SKEncodedImageFormat.Webp, ProductImageInputFormat.Webp)
                 })
        {
            var source = CreateImage(400, 200, item.Item1, transparent: false);
            var result = await processor.ProcessAsync(new MemoryStream(source), item.Item2, source.Length, 1_000_000);
            AssertJpeg(result.OriginalBytes, 400, 200);
            AssertJpeg(result.ThumbnailBytes, 256, 128);
            AssertSourceFingerprint(result);
        }

        var small = await processor.ProcessAsync(
            new MemoryStream(CreateImage(10, 5, SKEncodedImageFormat.Png, transparent: false)),
            ProductImageInputFormat.Png,
            1_000_000,
            1_000_000);
        Assert.AreEqual(10, small.ThumbnailWidth);
        Assert.AreEqual(5, small.ThumbnailHeight);
        AssertSourceFingerprint(small);
    }

    /// <summary>透明像素合成白底，编码结果不保留 Alpha。</summary>
    [TestMethod]
    public async Task TransparentPng_ShouldUseWhiteBackground()
    {
        using var bitmap = new SKBitmap(new SKImageInfo(16, 16, SKColorType.Bgra8888, SKAlphaType.Premul));
        bitmap.Erase(SKColors.Transparent);
        using (var canvas = new SKCanvas(bitmap))
        using (var paint = new SKPaint { Color = SKColors.Red })
            canvas.DrawRect(new SKRect(8, 0, 16, 16), paint);
        var source = Encode(bitmap, SKEncodedImageFormat.Png, 100);
        using var processor = new SkiaProductImageProcessor(1);

        var result = await processor.ProcessAsync(new MemoryStream(source), ProductImageInputFormat.Png, source.Length, 1_000);

        using var decoded = SKBitmap.Decode(result.OriginalBytes);
        var white = decoded.GetPixel(2, 8);
        Assert.IsTrue(white.Red > 235 && white.Green > 235 && white.Blue > 235);
        Assert.AreEqual((byte)255, white.Alpha);
    }

    /// <summary>EXIF 方向 6 在输出中交换长宽，证明像素已物理旋转而不是保留元数据。</summary>
    [TestMethod]
    public async Task ExifOrientation_ShouldBeNormalizedIntoPixels()
    {
        var jpeg = CreateImage(12, 5, SKEncodedImageFormat.Jpeg, transparent: false);
        var oriented = InsertExifOrientation(jpeg, orientation: 6);
        using var processor = new SkiaProductImageProcessor(1);

        var result = await processor.ProcessAsync(new MemoryStream(oriented), ProductImageInputFormat.Jpeg, oriented.Length, 1_000);

        Assert.AreEqual(5, result.OriginalWidth);
        Assert.AreEqual(12, result.OriginalHeight);
        AssertJpeg(result.OriginalBytes, 5, 12);
    }

    /// <summary>损坏、伪扩展名、像素超限和多帧输入返回稳定逐项错误。</summary>
    [TestMethod]
    public async Task InvalidInputs_ShouldReturnSpecificProcessingErrors()
    {
        using var processor = new SkiaProductImageProcessor(1);
        var mismatch = await Assert.ThrowsExactlyAsync<ProductImageProcessingException>(() =>
            processor.ProcessAsync(
                new MemoryStream(CreateImage(2, 2, SKEncodedImageFormat.Png, transparent: false)),
                ProductImageInputFormat.Jpeg,
                10_000,
                100));
        Assert.AreEqual(ProductImageErrorCodes.TypeMismatch, mismatch.ErrorCode);

        var pixels = await Assert.ThrowsExactlyAsync<ProductImageProcessingException>(() =>
            processor.ProcessAsync(
                new MemoryStream(CreateImage(20, 20, SKEncodedImageFormat.Jpeg, transparent: false)),
                ProductImageInputFormat.Jpeg,
                10_000,
                399));
        Assert.AreEqual(ProductImageErrorCodes.PixelLimitExceeded, pixels.ErrorCode);

        var animated = await Assert.ThrowsExactlyAsync<ProductImageProcessingException>(() =>
            processor.ProcessAsync(
                new MemoryStream(CreateTwoFrameAnimatedWebp()),
                ProductImageInputFormat.Webp,
                10_000,
                100));
        Assert.AreEqual(ProductImageErrorCodes.MultipleFrames, animated.ErrorCode);

        var corrupt = await Assert.ThrowsExactlyAsync<ProductImageProcessingException>(() =>
            processor.ProcessAsync(new MemoryStream([1, 2, 3, 4]), ProductImageInputFormat.Jpeg, 100, 100));
        Assert.AreEqual(ProductImageErrorCodes.ProcessingFailed, corrupt.ErrorCode);
    }

    /// <summary>处理容量满时不建立无界等待队列，并排空该文件节以允许 multipart 继续。</summary>
    [TestMethod]
    public async Task ProcessingCapacity_ShouldRejectAndDrainExcessWork()
    {
        var firstBytes = CreateImage(4, 4, SKEncodedImageFormat.Png, transparent: false);
        using var blocked = new BlockingReadStream(firstBytes);
        using var processor = new SkiaProductImageProcessor(1);
        var first = processor.ProcessAsync(
            blocked,
            ProductImageInputFormat.Png,
            firstBytes.Length,
            100);
        await blocked.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        using var rejectedSource = new MemoryStream(firstBytes);

        var rejected = await Assert.ThrowsExactlyAsync<ProductImageProcessingException>(() =>
            processor.ProcessAsync(
                rejectedSource,
                ProductImageInputFormat.Png,
                firstBytes.Length,
                100));

        Assert.AreEqual(ProductImageErrorCodes.ProcessingBusy, rejected.ErrorCode);
        Assert.AreEqual(rejectedSource.Length, rejectedSource.Position);
        blocked.AllowRead.TrySetResult();
        await first;
    }

    internal static byte[] CreateImage(
        int width,
        int height,
        SKEncodedImageFormat format,
        bool transparent)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
        bitmap.Erase(transparent ? SKColors.Transparent : SKColors.CornflowerBlue);
        return Encode(bitmap, format, 95);
    }

    private static byte[] Encode(SKBitmap bitmap, SKEncodedImageFormat format, int quality)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, quality);
        Assert.IsNotNull(data);
        return data.ToArray();
    }

    private static void AssertJpeg(byte[] bytes, int width, int height)
    {
        Assert.IsTrue(bytes.Length > 2 && bytes[0] == 0xFF && bytes[1] == 0xD8);
        using var bitmap = SKBitmap.Decode(bytes);
        Assert.IsNotNull(bitmap);
        Assert.AreEqual(width, bitmap.Width);
        Assert.AreEqual(height, bitmap.Height);
    }

    /// <summary>确认缩略图标签绑定最终原图字节，并使用处理器的唯一规格来源。</summary>
    private static void AssertSourceFingerprint(ProductImageProcessingResult result)
    {
        Assert.IsTrue(JpegThumbnailSourceFingerprintCodec.TryRead(result.ThumbnailBytes, out var fingerprint));
        Assert.IsNotNull(fingerprint);
        Assert.AreEqual(Convert.ToHexString(SHA256.HashData(result.OriginalBytes)), fingerprint.SourceSha256);
        Assert.AreEqual(ProductImageEncodingSpecification.ThumbnailSourceSpecification, fingerprint.Specification);
    }

    /// <summary>在 SOI 后插入最小 little-endian EXIF Orientation IFD，不改变原 JPEG 像素。</summary>
    private static byte[] InsertExifOrientation(byte[] jpeg, ushort orientation)
    {
        var app1 = new byte[]
        {
            0xFF, 0xE1, 0x00, 0x22,
            (byte)'E', (byte)'x', (byte)'i', (byte)'f', 0x00, 0x00,
            0x49, 0x49, 0x2A, 0x00, 0x08, 0x00, 0x00, 0x00,
            0x01, 0x00,
            0x12, 0x01, 0x03, 0x00, 0x01, 0x00, 0x00, 0x00,
            (byte)orientation, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00
        };
        var result = new byte[jpeg.Length + app1.Length];
        Buffer.BlockCopy(jpeg, 0, result, 0, 2);
        Buffer.BlockCopy(app1, 0, result, 2, app1.Length);
        Buffer.BlockCopy(jpeg, 2, result, 2 + app1.Length, jpeg.Length - 2);
        return result;
    }

    /// <summary>用两个真实 WebP 帧构造扩展动画容器，确保多帧门禁覆盖受支持的输入格式。</summary>
    private static byte[] CreateTwoFrameAnimatedWebp()
    {
        var firstFrame = ExtractWebpFrameChunks(CreateImage(2, 2, SKEncodedImageFormat.Webp, transparent: false));
        var secondFrame = ExtractWebpFrameChunks(CreateSolidWebp(2, 2, SKColors.OrangeRed));
        using var payload = new MemoryStream();
        using (var writer = new BinaryWriter(payload, System.Text.Encoding.ASCII, leaveOpen: true))
        {
            WriteChunk(writer, "VP8X", [0x02, 0, 0, 0, 1, 0, 0, 1, 0, 0]);
            WriteChunk(writer, "ANIM", [0, 0, 0, 0, 0, 0]);
            WriteAnimationFrame(writer, firstFrame);
            WriteAnimationFrame(writer, secondFrame);
        }

        using var result = new MemoryStream();
        using (var writer = new BinaryWriter(result, System.Text.Encoding.ASCII, leaveOpen: true))
        {
            writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(checked((uint)(payload.Length + 4)));
            writer.Write(System.Text.Encoding.ASCII.GetBytes("WEBP"));
            payload.Position = 0;
            payload.CopyTo(result);
        }

        return result.ToArray();
    }

    /// <summary>生成指定纯色的静态 WebP，作为动画容器中的独立帧数据。</summary>
    private static byte[] CreateSolidWebp(int width, int height, SKColor color)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
        bitmap.Erase(color);
        return Encode(bitmap, SKEncodedImageFormat.Webp, 95);
    }

    /// <summary>提取静态 WebP 的帧数据块，排除只能出现在顶层容器中的扩展头。</summary>
    private static byte[] ExtractWebpFrameChunks(byte[] webp)
    {
        using var result = new MemoryStream();
        var offset = 12;
        while (offset + 8 <= webp.Length)
        {
            var chunkName = System.Text.Encoding.ASCII.GetString(webp, offset, 4);
            var chunkLength = checked((int)BitConverter.ToUInt32(webp, offset + 4));
            var paddedLength = checked(chunkLength + (chunkLength & 1));
            if (chunkName is "ALPH" or "VP8 " or "VP8L")
                result.Write(webp, offset, checked(8 + paddedLength));
            offset = checked(offset + 8 + paddedLength);
        }

        if (result.Length == 0) Assert.Fail("测试未能从静态 WebP 提取帧数据块。");
        return result.ToArray();
    }

    /// <summary>按 WebP ANMF 布局写入一个 2×2、持续 100 毫秒且不偏移的动画帧。</summary>
    private static void WriteAnimationFrame(BinaryWriter writer, byte[] frameChunks)
    {
        using var frame = new MemoryStream();
        using (var frameWriter = new BinaryWriter(frame, System.Text.Encoding.ASCII, leaveOpen: true))
        {
            frameWriter.Write(new byte[6]);
            frameWriter.Write(new byte[] { 1, 0, 0, 1, 0, 0 });
            frameWriter.Write(new byte[] { 100, 0, 0, 0 });
            frameWriter.Write(frameChunks);
        }

        WriteChunk(writer, "ANMF", frame.ToArray());
    }

    /// <summary>写入 RIFF 块头、数据及偶数字节对齐填充。</summary>
    private static void WriteChunk(BinaryWriter writer, string name, byte[] data)
    {
        writer.Write(System.Text.Encoding.ASCII.GetBytes(name));
        writer.Write(checked((uint)data.Length));
        writer.Write(data);
        if ((data.Length & 1) != 0) writer.Write((byte)0);
    }

    /// <summary>在第一次读取前挂起，用于稳定占用一个处理槽。</summary>
    private sealed class BlockingReadStream(byte[] bytes) : Stream
    {
        private readonly MemoryStream _inner = new(bytes);
        private int _blocked;
        public TaskCompletionSource ReadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource AllowRead { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _blocked, 1) == 0)
            {
                ReadStarted.TrySetResult();
                await AllowRead.Task.WaitAsync(cancellationToken);
            }

            return await _inner.ReadAsync(buffer, cancellationToken);
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing) _inner.Dispose();
            base.Dispose(disposing);
        }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
