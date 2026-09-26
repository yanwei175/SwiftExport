using System.Buffers;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SwiftExport.Application.Interfaces.Files;
using SwiftExport.Application.Interfaces.Files.ProductImages;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Files.ProductImages;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Services.Files.ProductImages;

/// <summary>实现产品图片批次的逐项处理、同名互斥和原图/缩略图/配对记录顺序发布。</summary>
public sealed class ProductImageUploadService(
    IFileAccessPolicy accessPolicy,
    IProductImageStorageProvider storageProvider,
    IProductImageProcessor processor,
    ProductImageTargetLockManager targetLocks,
    ILogger<ProductImageUploadService> logger) : IProductImageUploadService
{
    private const string SpecificationVersion = "product-image-jpeg-v1";
    private const int OriginalJpegQuality = 90;
    private const int ThumbnailJpegQuality = 85;
    private const int ThumbnailMaxEdge = 256;
    private static readonly char[] InvalidNameCharacters = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];
    private static readonly HashSet<string> ReservedNames = BuildReservedNames();
    private static readonly JsonSerializerOptions MetadataJsonOptions = new() { WriteIndented = true };

    /// <inheritdoc />
    public async Task<ServiceResult<ProductImageBatchSession>> BeginBatchAsync(
        CancellationToken cancellationToken = default)
    {
        var access = await accessPolicy.AuthorizeWriteAsync(cancellationToken).ConfigureAwait(false);
        if (!access.Success)
        {
            return ServiceResult<ProductImageBatchSession>.Fail(
                access.Error?.Code ?? ServiceErrorCodes.Unexpected,
                access.Error?.Message ?? access.Message,
                access.Error?.Field);
        }

        if (!storageProvider.TryGetEnabledStorage(out var storage) || storage is null)
        {
            return ServiceResult<ProductImageBatchSession>.Fail(
                ServiceErrorCodes.ServiceUnavailable,
                "产品图片上传未启用。");
        }

        return ServiceResult<ProductImageBatchSession>.Ok(new ProductImageBatchSession(storage));
    }

    /// <inheritdoc />
    public async Task<ProductImageUploadItemResultDto> UploadItemAsync(
        ProductImageBatchSession session,
        int index,
        string inputFileName,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(content);

        if (!TryResolveTarget(inputFileName, out var targetFileName, out var expectedFormat))
        {
            await DrainAsync(content, cancellationToken).ConfigureAwait(false);
            return AddFailure(session, index, inputFileName, null,
                ProductImageErrorCodes.InvalidFileName, "文件名不能安全映射为产品图片。");
        }

        if (expectedFormat is null)
        {
            await DrainAsync(content, cancellationToken).ConfigureAwait(false);
            return AddFailure(session, index, inputFileName, targetFileName,
                ProductImageErrorCodes.UnsupportedType, "仅接受 JPEG、PNG 或 WebP 文件。");
        }

        if (!session.TryReserveTarget(targetFileName!))
        {
            await DrainAsync(content, cancellationToken).ConfigureAwait(false);
            return AddFailure(session, index, inputFileName, targetFileName,
                ProductImageErrorCodes.DuplicateTargetInBatch, "同一批次已有文件映射到该目标名称。");
        }

        ProductImageProcessingResult processed;
        try
        {
            processed = await processor.ProcessAsync(
                content,
                expectedFormat.Value,
                session.Storage.MaxFileBytes,
                session.Storage.MaxPixels,
                cancellationToken).ConfigureAwait(false);
        }
        catch (ProductImageProcessingException exception)
        {
            return AddFailure(session, index, inputFileName, targetFileName,
                exception.ErrorCode, exception.Message);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "产品图片转码失败，InputIndex={InputIndex}", index);
            return AddFailure(session, index, inputFileName, targetFileName,
                ProductImageErrorCodes.ProcessingFailed, "图片内容损坏或无法完成转码。");
        }

        return await PublishAsync(
            session,
            index,
            inputFileName,
            targetFileName!,
            processed,
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public ProductImageBatchUploadResultDto CompleteBatch(ProductImageBatchSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var items = session.Items.ToList();
        var succeeded = items.Count(item => item.Success);
        return new ProductImageBatchUploadResultDto
        {
            Total = items.Count,
            Succeeded = succeeded,
            Failed = items.Count - succeeded,
            Items = items
        };
    }

    /// <summary>在同名进程锁内先发布 pending，再依次发布两图，最后发布完成记录。</summary>
    private async Task<ProductImageUploadItemResultDto> PublishAsync(
        ProductImageBatchSession session,
        int index,
        string inputFileName,
        string targetFileName,
        ProductImageProcessingResult processed,
        CancellationToken cancellationToken)
    {
        var storage = session.Storage;
        var originalPath = $"{storage.ImageDirectoryName}/{targetFileName}";
        var thumbnailPath = $"{storage.ImageDirectoryName}/Thumbnails/{targetFileName}";
        var statePath = $"{storage.StateDirectoryPath}/{targetFileName}.json";
        var originalHash = Convert.ToHexString(SHA256.HashData(processed.OriginalBytes));
        var thumbnailHash = Convert.ToHexString(SHA256.HashData(processed.ThumbnailBytes));
        var generatedAtUtc = DateTimeOffset.UtcNow;
        var record = new ProductImagePairRecord
        {
            SchemaVersion = 1,
            Complete = false,
            TargetPath = originalPath,
            ThumbnailPath = thumbnailPath,
            OriginalSha256 = originalHash,
            ThumbnailSha256 = thumbnailHash,
            OriginalWidth = processed.OriginalWidth,
            OriginalHeight = processed.OriginalHeight,
            ThumbnailWidth = processed.ThumbnailWidth,
            ThumbnailHeight = processed.ThumbnailHeight,
            OriginalBytes = processed.OriginalBytes.LongLength,
            ThumbnailBytes = processed.ThumbnailBytes.LongLength,
            GeneratedAtUtc = generatedAtUtc,
            SpecificationVersion = SpecificationVersion,
            OriginalJpegQuality = OriginalJpegQuality,
            ThumbnailJpegQuality = ThumbnailJpegQuality,
            ThumbnailMaxEdge = ThumbnailMaxEdge,
            TransparentPixelsUseWhiteBackground = true,
            ExifOrientationNormalized = true
        };

        using var targetLock = await targetLocks.AcquireAsync(targetFileName, cancellationToken).ConfigureAwait(false);
        try
        {
            await WriteBytesAsync(storage.Storage, statePath, SerializeRecord(record), cancellationToken)
                .ConfigureAwait(false);
            await WriteBytesAsync(storage.Storage, originalPath, processed.OriginalBytes, cancellationToken)
                .ConfigureAwait(false);
            await WriteBytesAsync(storage.Storage, thumbnailPath, processed.ThumbnailBytes, cancellationToken)
                .ConfigureAwait(false);
            record.Complete = true;
            await WriteBytesAsync(storage.Storage, statePath, SerializeRecord(record), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // pending 已发布时保留它标记不完整状态；不删除可能已经生效的正式图片。
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "产品图片发布未完成，InputIndex={InputIndex}, Target={Target}", index, targetFileName);
            return AddFailure(session, index, inputFileName, targetFileName,
                ProductImageErrorCodes.PublishFailed, "图片未能完成发布；可重新上传同名文件修复。");
        }

        var result = new ProductImageUploadItemResultDto
        {
            Index = index,
            InputFileName = inputFileName,
            TargetFileName = targetFileName,
            Success = true,
            Message = "上传成功。",
            OriginalUrl = BuildPublicUrl(storage, targetFileName),
            ThumbnailUrl = BuildPublicUrl(storage, $"Thumbnails/{targetFileName}"),
            OriginalSha256 = originalHash,
            ThumbnailSha256 = thumbnailHash
        };
        session.Add(result);
        return result;
    }

    private static async Task WriteBytesAsync(
        DvFileStorage.Abstractions.IFileStorage storage,
        string path,
        byte[] bytes,
        CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        await storage.WriteAsync(path, stream, overwrite: true, cancellationToken).ConfigureAwait(false);
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

    /// <summary>严格校验客户端文件名，不通过 Path.GetFileName 静默修正目录或危险名称。</summary>
    private static bool TryResolveTarget(
        string inputFileName,
        out string? targetFileName,
        out ProductImageInputFormat? expectedFormat)
    {
        targetFileName = null;
        expectedFormat = null;
        if (string.IsNullOrEmpty(inputFileName) ||
            inputFileName.IndexOfAny(InvalidNameCharacters) >= 0 ||
            inputFileName.EndsWith(' ') || inputFileName.EndsWith('.') ||
            inputFileName.Any(character => character == '\0' || char.IsControl(character)))
            return false;

        var extensionIndex = inputFileName.LastIndexOf('.');
        if (extensionIndex <= 0 || extensionIndex == inputFileName.Length - 1) return false;
        var baseName = inputFileName[..extensionIndex];
        if (baseName.EndsWith(' ') || baseName.EndsWith('.') ||
            ReservedNames.Contains(baseName.Split('.', 2)[0]))
            return false;

        targetFileName = baseName + ".jpg";
        expectedFormat = inputFileName[(extensionIndex + 1)..].ToLowerInvariant() switch
        {
            "jpg" or "jpeg" => ProductImageInputFormat.Jpeg,
            "png" => ProductImageInputFormat.Png,
            "webp" => ProductImageInputFormat.Webp,
            _ => null
        };
        return true;
    }

    private static string BuildPublicUrl(ProductImageStorageContext storage, string relativePath)
    {
        var segments = relativePath.Split('/').Select(Uri.EscapeDataString);
        var relative = storage.PublicRequestPath.TrimStart('/') + "/" + string.Join("/", segments);
        return new Uri(storage.ApiBaseUri, relative).AbsoluteUri;
    }

    private static byte[] SerializeRecord(ProductImagePairRecord record) =>
        JsonSerializer.SerializeToUtf8Bytes(record, MetadataJsonOptions);

    private static ProductImageUploadItemResultDto AddFailure(
        ProductImageBatchSession session,
        int index,
        string inputFileName,
        string? targetFileName,
        string code,
        string message)
    {
        var result = new ProductImageUploadItemResultDto
        {
            Index = index,
            InputFileName = inputFileName,
            TargetFileName = targetFileName,
            Success = false,
            ErrorCode = code,
            Message = message
        };
        session.Add(result);
        return result;
    }

    private static HashSet<string> BuildReservedNames()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CON", "PRN", "AUX", "NUL", "CLOCK$", "CONIN$", "CONOUT$"
        };
        for (var index = 1; index <= 9; index++)
        {
            names.Add($"COM{index}");
            names.Add($"LPT{index}");
        }

        foreach (var suffix in new[] { '¹', '²', '³' })
        {
            names.Add($"COM{suffix}");
            names.Add($"LPT{suffix}");
        }

        return names;
    }

    private sealed class ProductImagePairRecord
    {
        public int SchemaVersion { get; set; }
        public bool Complete { get; set; }
        public required string TargetPath { get; set; }
        public required string ThumbnailPath { get; set; }
        public required string OriginalSha256 { get; set; }
        public required string ThumbnailSha256 { get; set; }
        public int OriginalWidth { get; set; }
        public int OriginalHeight { get; set; }
        public int ThumbnailWidth { get; set; }
        public int ThumbnailHeight { get; set; }
        public long OriginalBytes { get; set; }
        public long ThumbnailBytes { get; set; }
        public DateTimeOffset GeneratedAtUtc { get; set; }
        public required string SpecificationVersion { get; set; }
        public int OriginalJpegQuality { get; set; }
        public int ThumbnailJpegQuality { get; set; }
        public int ThumbnailMaxEdge { get; set; }
        public bool TransparentPixelsUseWhiteBackground { get; set; }
        public bool ExifOrientationNormalized { get; set; }
    }
}
