using System.Security.Cryptography;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SkiaSharp;
using SwiftExport.WebApi.Files.ProductImages;

namespace SwiftExport.Application.Tests.Files;

/// <summary>验证缩略图 JPEG COM 来源指纹协议的字节保持、严格解析和损坏边界。</summary>
[TestClass]
public sealed class JpegThumbnailSourceFingerprintTests
{
    /// <summary>嵌入标签后 JPEG 仍可解码，原字节顺序保持，且无关 COM 不影响读取。</summary>
    [TestMethod]
    public void EmbedAndRead_ShouldRoundTripWithoutReencodingJpeg()
    {
        var original = ProductImageProcessorTests.CreateImage(12, 7, SKEncodedImageFormat.Jpeg, transparent: false);
        var withUnrelatedComment = InsertCommentBeforeSos(original, Encoding.ASCII.GetBytes("unrelated-comment"));
        var sourceHash = SHA256.HashData(original);

        var tagged = JpegThumbnailSourceFingerprintCodec.Embed(withUnrelatedComment, sourceHash);

        using var decoded = SKBitmap.Decode(tagged);
        Assert.IsNotNull(decoded);
        Assert.AreEqual(12, decoded.Width);
        Assert.AreEqual(7, decoded.Height);
        Assert.IsTrue(JpegThumbnailSourceFingerprintCodec.TryRead(tagged, out var fingerprint));
        Assert.IsNotNull(fingerprint);
        Assert.AreEqual(Convert.ToHexString(sourceHash), fingerprint.SourceSha256);
        Assert.AreEqual(ProductImageEncodingSpecification.ThumbnailSourceSpecification, fingerprint.Specification);
        CollectionAssert.AreEqual(withUnrelatedComment, RemoveProtocolComment(tagged));
        Assert.ThrowsExactly<InvalidDataException>(() =>
            JpegThumbnailSourceFingerprintCodec.Embed(tagged, sourceHash));
    }

    /// <summary>缺失、未知版本、重复标签、非法 SHA/规格、截断和非法段长均不能确认匹配。</summary>
    [TestMethod]
    public void TryRead_ShouldRejectMissingUnknownDuplicateAndMalformedMetadata()
    {
        var jpeg = ProductImageProcessorTests.CreateImage(3, 2, SKEncodedImageFormat.Jpeg, transparent: false);
        var sourceHash = Convert.ToHexString(SHA256.HashData(jpeg));
        var validPayload = CreatePayload("v1", sourceHash, ProductImageEncodingSpecification.ThumbnailSourceSpecification);
        var tagged = InsertCommentBeforeSos(jpeg, validPayload);

        Assert.IsFalse(JpegThumbnailSourceFingerprintCodec.TryRead(jpeg, out _));
        Assert.IsFalse(JpegThumbnailSourceFingerprintCodec.TryRead(
            InsertCommentBeforeSos(jpeg, CreatePayload("v2", sourceHash, ProductImageEncodingSpecification.ThumbnailSourceSpecification)),
            out _));
        Assert.IsFalse(JpegThumbnailSourceFingerprintCodec.TryRead(
            InsertCommentBeforeSos(jpeg, CreatePayload("v1", sourceHash.ToLowerInvariant(), ProductImageEncodingSpecification.ThumbnailSourceSpecification)),
            out _));
        Assert.IsFalse(JpegThumbnailSourceFingerprintCodec.TryRead(
            InsertCommentBeforeSos(jpeg, CreatePayload("v1", sourceHash, "jpeg-v1;maxEdge=128")),
            out _));
        Assert.IsFalse(JpegThumbnailSourceFingerprintCodec.TryRead(
            InsertCommentBeforeSos(tagged, validPayload),
            out _));
        Assert.IsFalse(JpegThumbnailSourceFingerprintCodec.TryRead(tagged.AsSpan(0, tagged.Length - 1), out _));

        var invalidLength = tagged.ToArray();
        var protocolOffset = FindCommentOffset(invalidLength, ProtocolPrefixBytes);
        invalidLength[protocolOffset + 2] = 0;
        invalidLength[protocolOffset + 3] = 1;
        Assert.IsFalse(JpegThumbnailSourceFingerprintCodec.TryRead(invalidLength, out _));
    }

    /// <summary>协议文字即使出现在 SOS 后的扫描数据中也不会被当作头部 COM 标签。</summary>
    [TestMethod]
    public void TryRead_ShouldStopAtStartOfScan()
    {
        var jpeg = ProductImageProcessorTests.CreateImage(3, 2, SKEncodedImageFormat.Jpeg, transparent: false);
        var sourceHash = Convert.ToHexString(SHA256.HashData(jpeg));
        var payload = CreatePayload("v1", sourceHash, ProductImageEncodingSpecification.ThumbnailSourceSpecification);
        var comment = CreateCommentSegment(payload);
        var scanDataOffset = FindStartOfScanDataOffset(jpeg);
        var injected = InsertBytes(jpeg, scanDataOffset, comment);

        Assert.IsFalse(JpegThumbnailSourceFingerprintCodec.TryRead(injected, out _));
    }

    /// <summary>连续 marker 填充超过头部预算时，读取与写入均应安全拒绝而不继续扫描。</summary>
    [TestMethod]
    public void Codec_ShouldRejectMarkerFillBeyondHeaderBudget()
    {
        var jpeg = ProductImageProcessorTests.CreateImage(3, 2, SKEncodedImageFormat.Jpeg, transparent: false);
        var oversizedHeader = InsertBytes(jpeg, 2, Enumerable.Repeat((byte)0xFF, 1024 * 1024).ToArray());
        var sourceHash = SHA256.HashData(jpeg);

        Assert.IsFalse(JpegThumbnailSourceFingerprintCodec.TryRead(oversizedHeader, out _));
        Assert.ThrowsExactly<InvalidDataException>(() =>
            JpegThumbnailSourceFingerprintCodec.Embed(oversizedHeader, sourceHash));
    }

    private static readonly byte[] ProtocolPrefixBytes = Encoding.ASCII.GetBytes("SwiftExportThumbnail:");

    private static byte[] CreatePayload(string version, string sourceSha256, string specification) =>
        Encoding.ASCII.GetBytes(
            $"SwiftExportThumbnail:{version}\nSourceSha256={sourceSha256}\nSpec={specification}\n");

    private static byte[] InsertCommentBeforeSos(byte[] jpeg, byte[] payload) =>
        InsertBytes(jpeg, FindStartOfScanOffset(jpeg), CreateCommentSegment(payload));

    private static byte[] CreateCommentSegment(byte[] payload)
    {
        var length = checked(payload.Length + 2);
        return [0xFF, 0xFE, (byte)(length >> 8), (byte)length, .. payload];
    }

    /// <summary>删除首个属于本协议的 COM，用于证明 Embed 只插入 marker 而未改变原 JPEG。</summary>
    private static byte[] RemoveProtocolComment(byte[] jpeg)
    {
        var offset = FindCommentOffset(jpeg, ProtocolPrefixBytes);
        var length = (jpeg[offset + 2] << 8) | jpeg[offset + 3];
        return [.. jpeg.AsSpan(0, offset), .. jpeg.AsSpan(offset + 2 + length)];
    }

    private static int FindCommentOffset(byte[] jpeg, byte[] payloadPrefix)
    {
        var offset = 2;
        while (offset < jpeg.Length)
        {
            var markerOffset = offset;
            var marker = ReadMarker(jpeg, ref offset, out var payloadOffset, out var payloadLength);
            if (marker == 0xDA) break;
            if (marker == 0xFE && jpeg.AsSpan(payloadOffset, payloadLength).StartsWith(payloadPrefix))
                return markerOffset;
        }

        Assert.Fail("未找到预期 JPEG COM 标签。");
        return -1;
    }

    private static int FindStartOfScanOffset(byte[] jpeg)
    {
        var offset = 2;
        while (offset < jpeg.Length)
        {
            var markerOffset = offset;
            if (ReadMarker(jpeg, ref offset, out _, out _) == 0xDA) return markerOffset;
        }

        Assert.Fail("测试 JPEG 缺少 SOS。");
        return -1;
    }

    private static int FindStartOfScanDataOffset(byte[] jpeg)
    {
        var offset = 2;
        while (offset < jpeg.Length)
        {
            if (ReadMarker(jpeg, ref offset, out _, out _) == 0xDA) return offset;
        }

        Assert.Fail("测试 JPEG 缺少 SOS。");
        return -1;
    }

    /// <summary>仅为测试构造定位头部 marker，遇 SOS 后由调用方停止，不扫描熵编码数据。</summary>
    private static byte ReadMarker(
        byte[] jpeg,
        ref int offset,
        out int payloadOffset,
        out int payloadLength)
    {
        Assert.AreEqual((byte)0xFF, jpeg[offset]);
        var marker = jpeg[offset + 1];
        var length = (jpeg[offset + 2] << 8) | jpeg[offset + 3];
        Assert.IsGreaterThanOrEqualTo(2, length);
        payloadOffset = offset + 4;
        payloadLength = length - 2;
        offset += 2 + length;
        return marker;
    }

    private static byte[] InsertBytes(byte[] source, int offset, byte[] inserted) =>
        [.. source.AsSpan(0, offset), .. inserted, .. source.AsSpan(offset)];
}
