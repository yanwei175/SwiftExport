using System.Text;

namespace SwiftExport.WebApi.Files.ProductImages;

/// <summary>表示从缩略图 JPEG COM 段中确认读取的原图来源指纹和生成规格。</summary>
internal sealed class JpegThumbnailSourceFingerprint
{
    /// <summary>获取服务端最终原图完整 JPEG 字节的 SHA-256 大写十六进制值。</summary>
    public required string SourceSha256 { get; init; }

    /// <summary>获取生成缩略图时使用的稳定规格文本。</summary>
    public required string Specification { get; init; }
}

/// <summary>按 SwiftExport v1 协议在 JPEG 头部写入并读取单个 COM 来源指纹段。</summary>
internal static class JpegThumbnailSourceFingerprintCodec
{
    private const byte MarkerPrefix = 0xFF;
    private const byte StartOfImage = 0xD8;
    private const byte EndOfImage = 0xD9;
    private const byte StartOfScan = 0xDA;
    private const byte Comment = 0xFE;
    private const int MaximumCommentPayloadBytes = ushort.MaxValue - 2;
    private const int MaximumHeaderScanBytes = 1024 * 1024;
    private const string ProtocolNamespace = "SwiftExportThumbnail:";
    private const string ProtocolHeader = "SwiftExportThumbnail:v1";
    private const string SourcePrefix = "SourceSha256=";
    private const string SpecificationPrefix = "Spec=";
    private static readonly byte[] ProtocolNamespaceBytes = Encoding.ASCII.GetBytes(ProtocolNamespace);

    /// <summary>
    /// 在首个 SOS 前的合法 marker 边界插入 COM；只接受完整、无现有本协议标签的 JPEG。
    /// </summary>
    /// <param name="jpeg">处理器刚生成且需要保持编码数据不变的 JPEG 完整字节。</param>
    /// <param name="sourceSha256">最终原图完整 JPEG 字节的 32 字节 SHA-256。</param>
    /// <returns>保留原 JPEG 字节顺序并插入唯一来源标签的新字节数组。</returns>
    public static byte[] Embed(byte[] jpeg, ReadOnlySpan<byte> sourceSha256)
    {
        ArgumentNullException.ThrowIfNull(jpeg);
        if (sourceSha256.Length != 32)
            throw new ArgumentException("来源 SHA-256 必须正好为 32 字节。", nameof(sourceSha256));
        if (!TryFindInsertionOffset(jpeg, out var insertionOffset))
            throw new InvalidDataException("缩略图 JPEG 头部不完整、非法或已包含来源标签。");

        var sourceHex = Convert.ToHexString(sourceSha256);
        var payload = Encoding.ASCII.GetBytes(
            $"{ProtocolHeader}\n{SourcePrefix}{sourceHex}\n" +
            $"{SpecificationPrefix}{ProductImageEncodingSpecification.ThumbnailSourceSpecification}\n");
        if (payload.Length > MaximumCommentPayloadBytes)
            throw new InvalidDataException("缩略图来源标签超过 JPEG COM 容量。");

        var segmentLength = checked(payload.Length + 2);
        var result = new byte[checked(jpeg.Length + payload.Length + 4)];
        Buffer.BlockCopy(jpeg, 0, result, 0, insertionOffset);
        result[insertionOffset] = MarkerPrefix;
        result[insertionOffset + 1] = Comment;
        result[insertionOffset + 2] = (byte)(segmentLength >> 8);
        result[insertionOffset + 3] = (byte)segmentLength;
        Buffer.BlockCopy(payload, 0, result, insertionOffset + 4, payload.Length);
        Buffer.BlockCopy(
            jpeg,
            insertionOffset,
            result,
            insertionOffset + payload.Length + 4,
            jpeg.Length - insertionOffset);
        return result;
    }

    /// <summary>
    /// 在最多 1 MiB JPEG 头部内读取唯一且严格有效的 v1 标签；缺失、未知、重复或损坏均返回 false。
    /// </summary>
    public static bool TryRead(
        ReadOnlySpan<byte> jpeg,
        out JpegThumbnailSourceFingerprint? fingerprint)
    {
        fingerprint = null;
        if (!HasCompleteJpegBoundary(jpeg)) return false;

        var offset = 2;
        var sawFrameHeader = false;
        while (TryReadHeaderSegment(jpeg, ref offset, out var segment))
        {
            if (IsFrameHeader(segment.Marker)) sawFrameHeader = true;
            if (segment.Marker == StartOfScan)
                return sawFrameHeader && fingerprint is not null;
            if (segment.Marker is EndOfImage or StartOfImage || IsRestartMarker(segment.Marker))
                return false;
            if (segment.Marker != Comment) continue;

            var payload = jpeg.Slice(segment.PayloadOffset, segment.PayloadLength);
            var parseResult = TryParsePayload(payload, out var parsed);
            if (parseResult == PayloadParseResult.Irrelevant) continue;
            if (parseResult == PayloadParseResult.Invalid || fingerprint is not null) return false;
            fingerprint = parsed;
        }

        fingerprint = null;
        return false;
    }

    /// <summary>严格遍历新生成 JPEG 的头部，确认帧头、SOS 和 EOI 后返回插入位置。</summary>
    private static bool TryFindInsertionOffset(ReadOnlySpan<byte> jpeg, out int insertionOffset)
    {
        insertionOffset = 0;
        if (!HasCompleteJpegBoundary(jpeg)) return false;

        var offset = 2;
        var sawFrameHeader = false;
        while (TryReadHeaderSegment(jpeg, ref offset, out var segment))
        {
            if (IsFrameHeader(segment.Marker)) sawFrameHeader = true;
            if (segment.Marker == Comment)
            {
                var payload = jpeg.Slice(segment.PayloadOffset, segment.PayloadLength);
                if (payload.StartsWith(ProtocolNamespaceBytes)) return false;
            }

            if (segment.Marker == StartOfScan)
            {
                insertionOffset = segment.MarkerOffset;
                return sawFrameHeader;
            }

            if (segment.Marker is EndOfImage or StartOfImage || IsRestartMarker(segment.Marker)) return false;
        }

        return false;
    }

    /// <summary>读取一个头部 marker；所有长度均按 JPEG 两字节大端规则验证并受扫描预算约束。</summary>
    private static bool TryReadHeaderSegment(
        ReadOnlySpan<byte> jpeg,
        ref int offset,
        out HeaderSegment segment)
    {
        segment = default;
        if (offset < 2 || offset >= jpeg.Length || offset >= MaximumHeaderScanBytes ||
            jpeg[offset] != MarkerPrefix)
            return false;

        var markerOffset = offset;
        while (offset < jpeg.Length && offset < MaximumHeaderScanBytes &&
               jpeg[offset] == MarkerPrefix)
            offset++;
        if (offset >= jpeg.Length || offset >= MaximumHeaderScanBytes) return false;
        var marker = jpeg[offset++];
        if (marker == 0x00) return false;

        if (marker is StartOfImage or EndOfImage || IsRestartMarker(marker))
        {
            segment = new HeaderSegment(marker, markerOffset, offset, 0);
            return true;
        }

        if (offset + 2 > jpeg.Length) return false;
        var segmentLength = (jpeg[offset] << 8) | jpeg[offset + 1];
        if (segmentLength < 2 || segmentLength > jpeg.Length - offset) return false;
        var nextOffset = offset + segmentLength;
        if (nextOffset > MaximumHeaderScanBytes) return false;

        segment = new HeaderSegment(marker, markerOffset, offset + 2, segmentLength - 2);
        offset = nextOffset;
        return true;
    }

    /// <summary>区分无关 COM 与本协议，并只接受精确三行、固定规格和大写 SHA-256。</summary>
    private static PayloadParseResult TryParsePayload(
        ReadOnlySpan<byte> payload,
        out JpegThumbnailSourceFingerprint? fingerprint)
    {
        fingerprint = null;
        if (!payload.StartsWith(ProtocolNamespaceBytes)) return PayloadParseResult.Irrelevant;
        if (payload.IndexOfAnyExceptInRange((byte)0x00, (byte)0x7F) >= 0)
            return PayloadParseResult.Invalid;

        var text = Encoding.ASCII.GetString(payload);
        var lines = text.Split('\n');
        if (lines.Length != 4 || lines[3].Length != 0 ||
            lines[0] != ProtocolHeader ||
            !lines[1].StartsWith(SourcePrefix, StringComparison.Ordinal) ||
            !lines[2].StartsWith(SpecificationPrefix, StringComparison.Ordinal))
            return PayloadParseResult.Invalid;

        var sourceSha256 = lines[1][SourcePrefix.Length..];
        var specification = lines[2][SpecificationPrefix.Length..];
        if (!IsUpperHexSha256(sourceSha256) ||
            specification != ProductImageEncodingSpecification.ThumbnailSourceSpecification)
            return PayloadParseResult.Invalid;

        fingerprint = new JpegThumbnailSourceFingerprint
        {
            SourceSha256 = sourceSha256,
            Specification = specification
        };
        return PayloadParseResult.Valid;
    }

    private static bool HasCompleteJpegBoundary(ReadOnlySpan<byte> jpeg) =>
        jpeg.Length >= 6 && jpeg[0] == MarkerPrefix && jpeg[1] == StartOfImage &&
        jpeg[^2] == MarkerPrefix && jpeg[^1] == EndOfImage;

    private static bool IsUpperHexSha256(string value) =>
        value.Length == 64 && value.All(character =>
            character is >= '0' and <= '9' or >= 'A' and <= 'F');

    private static bool IsFrameHeader(byte marker) => marker is
        >= 0xC0 and <= 0xC3 or
        >= 0xC5 and <= 0xC7 or
        >= 0xC9 and <= 0xCB or
        >= 0xCD and <= 0xCF;

    private static bool IsRestartMarker(byte marker) => marker is 0x01 or >= 0xD0 and <= 0xD7;

    /// <summary>保存一个已完成边界校验的 JPEG 头部 marker 位置和 payload 范围。</summary>
    private readonly record struct HeaderSegment(
        byte Marker,
        int MarkerOffset,
        int PayloadOffset,
        int PayloadLength);

    /// <summary>区分无关注释、有效协议和属于本协议但格式无效的注释。</summary>
    private enum PayloadParseResult
    {
        Irrelevant,
        Valid,
        Invalid
    }
}
