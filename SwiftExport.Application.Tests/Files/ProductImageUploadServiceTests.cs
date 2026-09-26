using System.Collections.Concurrent;
using System.Text.Json;
using DvFileStorage.Abstractions;
using DvFileStorage.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Interfaces.Files;
using SwiftExport.Application.Interfaces.Files.ProductImages;
using SwiftExport.Application.Services.Files.ProductImages;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Files.ProductImages;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Tests.Files;

/// <summary>验证产品图片批次命名、部分成功、发布状态、指纹和同名进程锁。</summary>
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

    /// <summary>第二张正式文件发布失败时不误报成功，完成记录保持 pending；重传可修复并更新指纹。</summary>
    [TestMethod]
    public async Task PublishFailure_ShouldLeavePendingAndAllowRepair()
    {
        var storage = new InMemoryFileStorage { FailOnWriteNumber = 3 };
        var service = CreateService(storage, maxFileBytes: 10);
        var first = (await service.BeginBatchAsync()).Data;

        var failed = await service.UploadItemAsync(first, 0, "same.png", new MemoryStream([7]));

        Assert.IsFalse(failed.Success);
        Assert.AreEqual(ProductImageErrorCodes.PublishFailed, failed.ErrorCode);
        Assert.IsTrue(storage.Files.ContainsKey("ProductImages/same.jpg"));
        Assert.IsFalse(storage.Files.ContainsKey("ProductImages/Thumbnails/same.jpg"));
        Assert.IsFalse(ReadComplete(storage.Files[".product-image-state/product-images/same.jpg.json"]));

        var repair = (await service.BeginBatchAsync()).Data;
        var repaired = await service.UploadItemAsync(repair, 0, "same.png", new MemoryStream([9]));

        Assert.IsTrue(repaired.Success);
        Assert.IsTrue(ReadComplete(storage.Files[".product-image-state/product-images/same.jpg.json"]));
        Assert.AreEqual(repaired.OriginalSha256, Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(storage.Files["ProductImages/same.jpg"])));
        Assert.AreEqual(repaired.ThumbnailSha256, Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(storage.Files["ProductImages/Thumbnails/same.jpg"])));
    }

    /// <summary>两个批次并发覆盖同名目标时，最终完成记录必与两份最终文件属于同一版本。</summary>
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

        var metadata = storage.Files[".product-image-state/product-images/same.jpg.json"];
        using var document = JsonDocument.Parse(metadata);
        Assert.IsTrue(document.RootElement.GetProperty("Complete").GetBoolean());
        Assert.AreEqual(
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(storage.Files["ProductImages/same.jpg"])),
            document.RootElement.GetProperty("OriginalSha256").GetString());
        Assert.AreEqual(
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(storage.Files["ProductImages/Thumbnails/same.jpg"])),
            document.RootElement.GetProperty("ThumbnailSha256").GetString());
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

    private static ProductImageUploadService CreateService(IFileStorage storage, long maxFileBytes)
    {
        var context = new ProductImageStorageContext
        {
            Storage = storage,
            ImageDirectoryName = "ProductImages",
            StateDirectoryPath = ".product-image-state/product-images",
            ApiBaseUri = new Uri("https://example.test/erp/"),
            PublicRequestPath = "/files/ProductImages",
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
            NullLogger<ProductImageUploadService>.Instance);
    }

    private static bool ReadComplete(byte[] json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("Complete").GetBoolean();
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

    /// <summary>用输入首字节生成可区分的两份完整内容，隔离发布编排测试与 Skia 行为。</summary>
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
            return new ProductImageProcessingResult
            {
                OriginalBytes = [value, 0xA1],
                ThumbnailBytes = [value, 0xB2],
                OriginalWidth = 2,
                OriginalHeight = 1,
                ThumbnailWidth = 2,
                ThumbnailHeight = 1
            };
        }
    }

    /// <summary>提供原子字节快照和可控写入故障，用于观察三阶段非事务发布。</summary>
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
