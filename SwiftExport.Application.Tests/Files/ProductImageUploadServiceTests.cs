using System.Collections.Concurrent;
using System.Security.Cryptography;
using DvFileStorage.Abstractions;
using DvFileStorage.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SkiaSharp;
using SwiftExport.Application.Interfaces.Files;
using SwiftExport.Application.Interfaces.Files.ProductImages;
using SwiftExport.Application.Services.Files.ProductImages;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Files.ProductImages;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.WebApi.Files.ProductImages;

namespace SwiftExport.Application.Tests.Files;

/// <summary>验证产品图片批次命名、部分成功、双文件发布、来源指纹和同名进程锁。</summary>
[TestClass]
public sealed class ProductImageUploadServiceTests
{
    /// <summary>非法名、单文件超限和转换后同名只影响各自项目，结果顺序与计数稳定。</summary>
    [TestMethod]
    public async Task BatchItems_ShouldAllowPartialSuccessAndRejectDuplicateTargets()
    {
        var storage = new InMemoryFileStorage();
        var service = CreateService(storage, maxFileBytes: 2);
        var session = (await service.BeginBatchAsync()).Data;

        await service.UploadItemAsync(session, 0, "A.png", new MemoryStream([1]));
        await service.UploadItemAsync(session, 1, "a.jpg", new MemoryStream([2]));
        await service.UploadItemAsync(session, 2, "../bad.png", new MemoryStream([3]));
        await service.UploadItemAsync(session, 3, "large.webp", new MemoryStream([1, 2, 3]));
        var result = service.CompleteBatch(session);

        Assert.AreEqual(4, result.Total);
        Assert.AreEqual(1, result.Succeeded);
        Assert.AreEqual(3, result.Failed);
        CollectionAssert.AreEqual(new[] { 0, 1, 2, 3 }, result.Items.Select(item => item.Index).ToArray());
        Assert.AreEqual(ProductImageErrorCodes.DuplicateTargetInBatch, result.Items[1].ErrorCode);
        Assert.AreEqual(ProductImageErrorCodes.InvalidFileName, result.Items[2].ErrorCode);
        Assert.AreEqual(ProductImageErrorCodes.FileTooLarge, result.Items[3].ErrorCode);
        Assert.IsTrue(storage.Files.ContainsKey("ProductImages/A.jpg"));
    }

    /// <summary>缩略图发布失败时不误报成功、不写 JSON；重传可修复并建立来源指纹。</summary>
    [TestMethod]
    public async Task ThumbnailPublishFailure_ShouldNotReportSuccessAndRetryRepairsPair()
    {
        var storage = new InMemoryFileStorage { FailOnWriteNumber = 2 };
        var service = CreateService(storage, maxFileBytes: 10);
        var first = (await service.BeginBatchAsync()).Data;

        var failed = await service.UploadItemAsync(first, 0, "same.png", new MemoryStream([7]));

        Assert.IsFalse(failed.Success);
        Assert.AreEqual(ProductImageErrorCodes.PublishFailed, failed.ErrorCode);
        Assert.IsTrue(storage.Files.ContainsKey("ProductImages/same.jpg"));
        Assert.IsFalse(storage.Files.ContainsKey("ProductImages/Thumbnails/same.jpg"));
        Assert.IsFalse(storage.Files.Keys.Any(path => path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)));

        var repair = (await service.BeginBatchAsync()).Data;
        var repaired = await service.UploadItemAsync(repair, 0, "same.png", new MemoryStream([9]));

        Assert.IsTrue(repaired.Success);
        Assert.AreEqual(repaired.OriginalSha256, Convert.ToHexString(
            SHA256.HashData(storage.Files["ProductImages/same.jpg"])));
        Assert.AreEqual(repaired.ThumbnailSha256, Convert.ToHexString(
            SHA256.HashData(storage.Files["ProductImages/Thumbnails/same.jpg"])));
        AssertThumbnailMatchesSource(storage, "same.jpg");
    }

    /// <summary>两个批次并发覆盖同名目标时，最终缩略图标签必与最终原图属于同一版本。</summary>
    [TestMethod]
    public async Task ConcurrentSameTarget_ShouldNotPublishMixedCompletedRecord()
    {
        var storage = new InMemoryFileStorage { WriteDelay = TimeSpan.FromMilliseconds(10) };
        var service = CreateService(storage, maxFileBytes: 10);
        var first = (await service.BeginBatchAsync()).Data;
        var second = (await service.BeginBatchAsync()).Data;

        await Task.WhenAll(
            service.UploadItemAsync(first, 0, "same.png", new MemoryStream([1])),
            service.UploadItemAsync(second, 0, "SAME.png", new MemoryStream([2])));

        AssertThumbnailMatchesSource(storage, "same.jpg");
        Assert.IsFalse(storage.Files.Keys.Any(path => path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>不同目标使用独立锁，持有一个目标不会阻断另一个目标进入发布区。</summary>
    [TestMethod]
    public async Task DifferentTargets_ShouldNotUseGlobalPublicationLock()
    {
        var manager = new ProductImageTargetLockManager();
        using var first = await manager.AcquireAsync("a.jpg", CancellationToken.None);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        using var second = await manager.AcquireAsync("b.jpg", timeout.Token);
        Assert.IsNotNull(second);
    }

    /// <summary>缺失基址拒绝整个批次，尚未执行转码或文件发布。</summary>
    [TestMethod]
    [DataRow(null)]
    [DataRow("ftp://example.test/")]
    public async Task MissingOrInvalidParameter_ShouldRejectBeforePublication(string? value)
    {
        var storage = new InMemoryFileStorage();
        var service = CreateService(storage, 10, new SystemParameterRepositoryStub(value));
        Assert.AreEqual(ServiceErrorCodes.ServiceUnavailable, (await service.BeginBatchAsync()).Error.Code);
        Assert.AreEqual(0, storage.Files.Count);
    }

    /// <summary>批次中途修改参数只影响下个批次，原图和缩略图回执使用相同快照。</summary>
    [TestMethod]
    public async Task Batch_ShouldKeepOneBaseUrlSnapshotAndPreserveDots()
    {
        var storage = new InMemoryFileStorage();
        var parameters = new SystemParameterRepositoryStub();
        var service = CreateService(storage, 10, parameters);
        var first = (await service.BeginBatchAsync()).Data;
        parameters.Value = "https://new.test/catalog/";
        var result = await service.UploadItemAsync(first, 0, "中文 #%.001.png", new MemoryStream([1]));
        Assert.IsTrue(result.Success);
        Assert.AreEqual("https://example.test/erp/files/ProductImages/%E4%B8%AD%E6%96%87%20%23%25.001.jpg", result.OriginalUrl);
        Assert.AreEqual("https://example.test/erp/files/ProductImages/Thumbnails/%E4%B8%AD%E6%96%87%20%23%25.001.jpg", result.ThumbnailUrl);
        Assert.AreEqual(1, parameters.Reads);
        var second = (await service.BeginBatchAsync()).Data;
        var next = await service.UploadItemAsync(second, 0, "ABC.001.jpg", new MemoryStream([2]));
        Assert.AreEqual("https://new.test/catalog/ABC.001.jpg", next.OriginalUrl);
    }

    private static ProductImageUploadService CreateService(IFileStorage storage, long maxFileBytes, SystemParameterRepositoryStub? parameters = null)
    {
        var context = new ProductImageStorageContext
        {
            Storage = storage,
            ImageDirectoryName = "ProductImages",
            MaxFileBytes = maxFileBytes,
            MaxBatchBytes = 1000,
            MaxFiles = 20,
            MaxPixels = 1000
        };
        return new ProductImageUploadService(
            new AllowAdminPolicy(),
            new StorageProvider(context),
            new EchoProcessor(),
            new ProductImageTargetLockManager(),
            NullLogger<ProductImageUploadService>.Instance,
            parameters ?? new SystemParameterRepositoryStub());
    }

    /// <summary>读取最终缩略图标签并确认其来源哈希等于当前最终原图。</summary>
    private static void AssertThumbnailMatchesSource(InMemoryFileStorage storage, string fileName)
    {
        var original = storage.Files[$"ProductImages/{fileName}"];
        var thumbnail = storage.Files[$"ProductImages/Thumbnails/{fileName}"];
        Assert.IsTrue(JpegThumbnailSourceFingerprintCodec.TryRead(thumbnail, out var fingerprint));
        Assert.IsNotNull(fingerprint);
        Assert.AreEqual(Convert.ToHexString(SHA256.HashData(original)), fingerprint.SourceSha256);
        Assert.AreEqual(ProductImageEncodingSpecification.ThumbnailSourceSpecification, fingerprint.Specification);
    }

    private sealed class AllowAdminPolicy : IFileAccessPolicy
    {
        public Task<ServiceResult<bool>> AuthorizeReadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(ServiceResult<bool>.Ok(true));

        public Task<ServiceResult<bool>> AuthorizeWriteAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(ServiceResult<bool>.Ok(true));
    }

    private sealed class StorageProvider(ProductImageStorageContext context) : IProductImageStorageProvider
    {
        public bool TryGetEnabledStorage(out ProductImageStorageContext? storage)
        {
            storage = context;
            return true;
        }
    }

    /// <summary>用输入首字节生成可区分且带正确来源标签的 JPEG，隔离发布编排与真实转码规则。</summary>
    private sealed class EchoProcessor : IProductImageProcessor
    {
        public async Task<ProductImageProcessingResult> ProcessAsync(
            Stream source,
            ProductImageInputFormat expectedFormat,
            long maxFileBytes,
            long maxPixels,
            CancellationToken cancellationToken = default)
        {
            using var content = new MemoryStream();
            await source.CopyToAsync(content, cancellationToken);
            if (content.Length > maxFileBytes)
                throw new ProductImageProcessingException(
                    ProductImageErrorCodes.FileTooLarge,
                    "文件超过单张上传大小上限。");
            var value = content.ToArray()[0];
            var originalBytes = CreateJpeg(new SKColor(value, 32, 64));
            var thumbnailBytes = CreateJpeg(new SKColor(64, value, 32));
            thumbnailBytes = JpegThumbnailSourceFingerprintCodec.Embed(
                thumbnailBytes,
                SHA256.HashData(originalBytes));
            return new ProductImageProcessingResult
            {
                OriginalBytes = originalBytes,
                ThumbnailBytes = thumbnailBytes,
                OriginalWidth = 2,
                OriginalHeight = 1,
                ThumbnailWidth = 2,
                ThumbnailHeight = 1
            };
        }

        private static byte[] CreateJpeg(SKColor color)
        {
            using var bitmap = new SKBitmap(2, 1);
            bitmap.Erase(color);
            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Jpeg, 90);
            return data.ToArray();
        }
    }

    /// <summary>提供原子字节快照和可控写入故障，用于观察双文件非事务发布。</summary>
    private sealed class InMemoryFileStorage : IFileStorage
    {
        private int _writeCount;
        public ConcurrentDictionary<string, byte[]> Files { get; } = new(StringComparer.OrdinalIgnoreCase);
        public int? FailOnWriteNumber { get; init; }
        public TimeSpan WriteDelay { get; init; }

        public async Task WriteAsync(
            string path,
            Stream source,
            bool overwrite = false,
            CancellationToken cancellationToken = default)
        {
            using var destination = new MemoryStream();
            await source.CopyToAsync(destination, cancellationToken);
            if (WriteDelay > TimeSpan.Zero) await Task.Delay(WriteDelay, cancellationToken);
            var number = Interlocked.Increment(ref _writeCount);
            if (number == FailOnWriteNumber) throw new IOException("injected");
            Files[path] = destination.ToArray();
        }

        public Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream>(new MemoryStream(Files[path], writable: false));

        public Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default) =>
            Task.FromResult(Files.ContainsKey(path));

        public Task<StoredFileInfo?> GetInfoAsync(string path, CancellationToken cancellationToken = default) =>
            Task.FromResult(Files.TryGetValue(path, out var bytes)
                ? new StoredFileInfo { Path = path, Length = bytes.LongLength, LastModifiedUtc = DateTimeOffset.UtcNow }
                : null);

        public Task DeleteIfExistsAsync(string path, CancellationToken cancellationToken = default)
        {
            Files.TryRemove(path, out _);
            return Task.CompletedTask;
        }
    }
}
