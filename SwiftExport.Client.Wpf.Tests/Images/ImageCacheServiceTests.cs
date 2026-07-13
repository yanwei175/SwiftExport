using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExportClient.Services.Images;

namespace SwiftExport.Client.Wpf.Tests.Images;

[TestClass]
public sealed class ImageCacheServiceTests
{
    [TestMethod]
    public async Task GetAsync_ShouldFreezeBitmapDeduplicateDecodeAndReleaseSourceFile()
    {
        await using var scope = new ImageTestScope();
        var source = scope.CreateImage("source.png", 256, 256, seed: 1);
        using var service = scope.CreateService();

        var results = await Task.WhenAll(
            service.GetAsync(source, 96).AsTask(),
            service.GetAsync(source, 96).AsTask());

        Assert.IsNotNull(results[0]);
        Assert.IsTrue(results[0]!.IsFrozen);
        Assert.AreSame(results[0], results[1]);
        Assert.AreEqual(1L, service.GetStatistics().DecodeCount);

        var moved = Path.Combine(scope.Root, "moved.png");
        File.Move(source, moved);
        Assert.IsTrue(File.Exists(moved), "OnLoad 后不得继续锁定原始图片。");
    }

    [TestMethod]
    public async Task GetAsync_ShouldRejectRelativeHttpMissingCorruptAndOversizedSource()
    {
        await using var scope = new ImageTestScope();
        var corrupt = Path.Combine(scope.Root, "corrupt.png");
        await File.WriteAllTextAsync(corrupt, "not an image");
        var oversized = Path.Combine(scope.Root, "oversized.bin");
        await File.WriteAllBytesAsync(oversized, new byte[1024 * 1024 + 1]);
        using var service = scope.CreateService(options => options.MaximumSourceFileMegabytes = 1);

        Assert.IsNull(await service.GetAsync("relative.png", 96));
        Assert.IsNull(await service.GetAsync("https://example.test/image.png", 96));
        Assert.IsNull(await service.GetAsync(Path.Combine(scope.Root, "missing.png"), 96));
        Assert.IsNull(await service.GetAsync(corrupt, 96));
        Assert.IsNull(await service.GetAsync(oversized, 96));
    }

    [TestMethod]
    public async Task MemoryCache_ShouldRespectEntryLimitAndUseLru()
    {
        await using var scope = new ImageTestScope();
        using var service = scope.CreateService(options => options.MemoryEntryLimit = 2);
        var first = scope.CreateImage("1.png", 128, 128, 1);
        var second = scope.CreateImage("2.png", 128, 128, 2);
        var third = scope.CreateImage("3.png", 128, 128, 3);

        await service.GetAsync(first, 96);
        await service.GetAsync(second, 96);
        await service.GetAsync(first, 96);
        await service.GetAsync(third, 96);

        var statistics = service.GetStatistics();
        Assert.AreEqual(2, statistics.MemoryEntryCount);
        Assert.IsTrue(statistics.MemoryBytes <= 64L * 1024 * 1024);
        Assert.IsTrue(statistics.MemoryHits >= 1);
    }

    [TestMethod]
    public async Task DiskCache_ShouldApplyQuotaAndExpiration()
    {
        await using var scope = new ImageTestScope();
        var source = scope.CreateImage("large.png", 1024, 1024, seed: 17);
        using (var quotaService = scope.CreateService(options =>
               {
                   options.DiskLimitMegabytes = 1;
                   options.MaximumDecodePixelWidth = 2048;
               }))
        {
            await quotaService.GetAsync(source, 1024);
            Assert.IsTrue(quotaService.GetStatistics().DiskBytes <= 1024L * 1024L * 0.8);
        }

        var small = scope.CreateImage("small.png", 128, 128, seed: 3);
        using (var firstService = scope.CreateService())
        {
            await firstService.GetAsync(small, 96);
        }

        foreach (var file in Directory.EnumerateFiles(scope.CacheRoot, "*.png"))
        {
            File.SetLastAccessTimeUtc(file, DateTime.UtcNow.AddDays(-31));
        }

        using var expiredService = scope.CreateService();
        await expiredService.GetAsync(small, 96);
        Assert.AreEqual(1L, expiredService.GetStatistics().DecodeCount, "过期缩略图应删除并重新解码。");
    }

    [TestMethod]
    public async Task SourceMetadataChange_ShouldInvalidatePreviousThumbnail()
    {
        await using var scope = new ImageTestScope();
        var source = scope.CreateImage("source.png", 128, 128, seed: 1);
        using var service = scope.CreateService();
        await service.GetAsync(source, 96);

        scope.CreateImage("source.png", 128, 128, seed: 2);
        File.SetLastWriteTimeUtc(source, DateTime.UtcNow.AddSeconds(2));
        await service.GetAsync(source, 96);

        Assert.AreEqual(2L, service.GetStatistics().DecodeCount);
    }

    [TestMethod]
    public async Task DiskCache_ShouldPublishAfterClosingTemporaryStreamAndBeReusable()
    {
        await using var scope = new ImageTestScope();
        var source = scope.CreateImage("source.png", 256, 256, seed: 11);

        using (var writer = scope.CreateService())
        {
            Assert.IsNotNull(await writer.GetAsync(source, 96));
        }

        Assert.AreEqual(1, Directory.EnumerateFiles(scope.CacheRoot, "*.png").Count());
        Assert.AreEqual(0, Directory.EnumerateFiles(scope.CacheRoot, "*.tmp").Count());

        using var reader = scope.CreateService();
        Assert.IsNotNull(await reader.GetAsync(source, 96));
        Assert.AreEqual(1L, reader.GetStatistics().DiskHits);
        Assert.AreEqual(0L, reader.GetStatistics().DecodeCount);
    }

    [TestMethod]
    public async Task DiskCache_ConcurrentServiceInstances_ShouldPublishSingleFileWithoutTemporaryResidue()
    {
        await using var scope = new ImageTestScope();
        var source = scope.CreateImage("source.png", 256, 256, seed: 23);
        using var first = scope.CreateService();
        using var second = scope.CreateService();

        var results = await Task.WhenAll(
            first.GetAsync(source, 96).AsTask(),
            second.GetAsync(source, 96).AsTask());

        Assert.IsTrue(results.All(bitmap => bitmap is not null));
        Assert.AreEqual(1, Directory.EnumerateFiles(scope.CacheRoot, "*.png").Count());
        Assert.AreEqual(0, Directory.EnumerateFiles(scope.CacheRoot, "*.tmp").Count());
    }

    private sealed class ImageTestScope : IAsyncDisposable
    {
        public ImageTestScope()
        {
            Root = Path.Combine(Path.GetTempPath(), "SwiftExport.ImageTests", Guid.NewGuid().ToString("N"));
            CacheRoot = Path.Combine(Root, "cache");
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public string CacheRoot { get; }

        public ImageCacheService CreateService(Action<ImageCacheOptions>? configure = null)
        {
            var options = new ImageCacheOptions { DiskCachePath = CacheRoot };
            configure?.Invoke(options);
            return new ImageCacheService(options);
        }

        public string CreateImage(string fileName, int width, int height, int seed)
        {
            var path = Path.Combine(Root, fileName);
            var pixels = new byte[width * height * 4];
            new Random(seed).NextBytes(pixels);
            var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
            encoder.Save(stream);
            return path;
        }

        public ValueTask DisposeAsync()
        {
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch (IOException)
            {
            }

            return ValueTask.CompletedTask;
        }
    }
}
