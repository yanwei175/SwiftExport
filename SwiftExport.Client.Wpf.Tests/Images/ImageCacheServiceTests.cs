using System.IO;
using System.Net;
using System.Net.Http;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExportClient.Services.Images;

namespace SwiftExport.Client.Wpf.Tests.Images;

[TestClass]
public sealed class ImageCacheServiceTests
{
    /// <summary>验证网络图片按完整地址和宽度缓存，且网络分支不写磁盘。</summary>
    [TestMethod]
    public async Task HttpGetAsync_ShouldDecodeCacheByExactUrlAndWidthWithoutWritingDisk()
    {
        await using var scope = new ImageTestScope();
        var imageBytes = scope.CreateImageBytes(128, 128, seed: 31);
        var handler = new RecordingHttpMessageHandler((_, _) =>
            Task.FromResult(Response(HttpStatusCode.OK, new ByteArrayContent(imageBytes))));
        using var service = scope.CreateHttpService(handler);

        var first = await service.GetAsync("https://images.test/Case.png?version=1", 96);
        var cached = await service.GetAsync("https://images.test/Case.png?version=1", 96);
        var differentCase = await service.GetAsync("https://images.test/case.png?version=1", 96);
        var differentWidth = await service.GetAsync("https://images.test/Case.png?version=1", 48);

        Assert.IsNotNull(first);
        Assert.IsTrue(first.IsFrozen);
        Assert.AreSame(first, cached);
        Assert.IsNotNull(differentCase);
        Assert.IsNotNull(differentWidth);
        Assert.AreEqual(3, handler.RequestCount, "相同键应命中内存，不同路径大小写或宽度必须独立请求。");
        Assert.IsFalse(Directory.Exists(scope.CacheRoot) && Directory.EnumerateFiles(scope.CacheRoot).Any(),
            "网络图片不得生成磁盘缓存或临时文件。");
    }

    /// <summary>验证网络内存缓存到期后会重新请求并解码同一地址。</summary>
    [TestMethod]
    public async Task HttpMemoryCache_ShouldReloadAfterTtl()
    {
        await using var scope = new ImageTestScope();
        var clock = new ManualTimeProvider();
        var requestNumber = 0;
        var handler = new RecordingHttpMessageHandler((_, _) =>
        {
            var bytes = scope.CreateImageBytes(64, 64, Interlocked.Increment(ref requestNumber));
            return Task.FromResult(Response(HttpStatusCode.OK, new ByteArrayContent(bytes)));
        });
        using var service = scope.CreateHttpService(handler, clock, options => options.HttpMemoryCacheSeconds = 60);

        var first = await service.GetAsync("https://images.test/replaceable.png", 48);
        var cached = await service.GetAsync("https://images.test/replaceable.png", 48);
        clock.Advance(TimeSpan.FromSeconds(61));
        var refreshed = await service.GetAsync("https://images.test/replaceable.png", 48);

        Assert.AreSame(first, cached);
        Assert.AreNotSame(first, refreshed);
        Assert.AreEqual(2, handler.RequestCount);
    }

    /// <summary>验证 HTTP 失败、损坏内容、读取超时及已知/未知长度超限均返回空图。</summary>
    [TestMethod]
    public async Task HttpGetAsync_ShouldReturnNullForStatusCorruptionTimeoutAndOverflow()
    {
        await using var scope = new ImageTestScope();
        var oversized = new byte[1024 * 1024 + 1];
        var handler = new RecordingHttpMessageHandler(async (request, cancellationToken) =>
        {
            return request.RequestUri!.AbsolutePath switch
            {
                "/missing.png" => Response(HttpStatusCode.NotFound),
                "/corrupt.png" => Response(HttpStatusCode.OK, new ByteArrayContent("not an image"u8.ToArray())),
                "/known-oversized.png" => Response(HttpStatusCode.OK, new ByteArrayContent(oversized)),
                "/unknown-oversized.png" => Response(HttpStatusCode.OK, new UnknownLengthContent(oversized)),
                "/timeout.png" => await WaitForTimeoutAsync(cancellationToken),
                _ => Response(HttpStatusCode.InternalServerError),
            };
        });
        using var service = scope.CreateHttpService(handler, configure: options =>
        {
            options.MaximumSourceFileMegabytes = 1;
            options.HttpTimeoutSeconds = 1;
        });

        Assert.IsNull(await service.GetAsync("https://images.test/missing.png", 96));
        Assert.IsNull(await service.GetAsync("https://images.test/missing.png", 96));
        Assert.IsNull(await service.GetAsync("https://images.test/corrupt.png", 96));
        Assert.IsNull(await service.GetAsync("https://images.test/known-oversized.png", 96));
        Assert.IsNull(await service.GetAsync("https://images.test/unknown-oversized.png", 96));
        Assert.IsNull(await service.GetAsync("https://images.test/timeout.png", 96));
        Assert.AreEqual(5, handler.RequestCount, "相同失败键应命中短负缓存，不重复发送请求。");
        Assert.IsFalse(Directory.Exists(scope.CacheRoot) && Directory.EnumerateFiles(scope.CacheRoot).Any());
    }

    /// <summary>验证同键请求共享，且单个消费者取消不会终止其他消费者。</summary>
    [TestMethod]
    public async Task HttpInflight_ShouldSurviveSingleConsumerCancellation()
    {
        await using var scope = new ImageTestScope();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var imageBytes = scope.CreateImageBytes(96, 96, seed: 47);
        var handler = new RecordingHttpMessageHandler(async (_, cancellationToken) =>
        {
            started.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
            return Response(HttpStatusCode.OK, new ByteArrayContent(imageBytes));
        });
        using var service = scope.CreateHttpService(handler);
        using var cancellation = new CancellationTokenSource();

        var canceledConsumer = service.GetAsync("https://images.test/shared.png", 96, cancellation.Token).AsTask();
        var survivingConsumer = service.GetAsync("https://images.test/shared.png", 96).AsTask();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(1));
        cancellation.Cancel();
        release.TrySetResult();

        await Assert.ThrowsAsync<OperationCanceledException>(() => canceledConsumer);
        Assert.IsNotNull(await survivingConsumer);
        Assert.AreEqual(1, handler.RequestCount, "同键消费者必须共享一个网络请求。");
    }

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
    public async Task GetAsync_ShouldRejectRelativeMissingCorruptAndOversizedSource()
    {
        await using var scope = new ImageTestScope();
        var corrupt = Path.Combine(scope.Root, "corrupt.png");
        await File.WriteAllTextAsync(corrupt, "not an image");
        var oversized = Path.Combine(scope.Root, "oversized.bin");
        await File.WriteAllBytesAsync(oversized, new byte[1024 * 1024 + 1]);
        using var service = scope.CreateService(options => options.MaximumSourceFileMegabytes = 1);

        Assert.IsNull(await service.GetAsync("relative.png", 96));
        Assert.IsNull(await service.GetAsync("relative/image.png", 96));
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
        private readonly List<HttpClient> _httpClients = [];

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

        /// <summary>创建使用受控 HTTP 处理器和时间源的图片缓存。</summary>
        public ImageCacheService CreateHttpService(
            HttpMessageHandler handler,
            TimeProvider? timeProvider = null,
            Action<ImageCacheOptions>? configure = null)
        {
            var options = new ImageCacheOptions { DiskCachePath = CacheRoot };
            configure?.Invoke(options);
            var client = new HttpClient(handler, disposeHandler: false)
            {
                Timeout = Timeout.InfiniteTimeSpan,
            };
            _httpClients.Add(client);
            return new ImageCacheService(options, client, timeProvider);
        }

        /// <summary>创建可供网络响应返回的内存 PNG 数据。</summary>
        public byte[] CreateImageBytes(int width, int height, int seed)
        {
            var pixels = new byte[width * height * 4];
            new Random(seed).NextBytes(pixels);
            var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            return stream.ToArray();
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
            foreach (var client in _httpClients)
            {
                client.Dispose();
            }

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

    private static HttpResponseMessage Response(HttpStatusCode statusCode, HttpContent? content = null) => new(statusCode)
    {
        Content = content ?? new ByteArrayContent([]),
    };

    private static async Task<HttpResponseMessage> WaitForTimeoutAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        return Response(HttpStatusCode.OK);
    }

    /// <summary>记录请求次数并把响应交给测试委托生成。</summary>
    private sealed class RecordingHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsync) : HttpMessageHandler
    {
        private int _requestCount;

        /// <summary>获取已发送的 HTTP 请求数量。</summary>
        public int RequestCount => Volatile.Read(ref _requestCount);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requestCount);
            return sendAsync(request, cancellationToken);
        }
    }

    /// <summary>模拟未声明 Content-Length 的分块响应体。</summary>
    private sealed class UnknownLengthContent(byte[] bytes) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            stream.WriteAsync(bytes).AsTask();

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    /// <summary>为网络内存缓存 TTL 测试提供可推进的 UTC 时间。</summary>
    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow = new(2026, 9, 26, 0, 0, 0, TimeSpan.Zero);

        /// <summary>获取测试当前 UTC 时间。</summary>
        public override DateTimeOffset GetUtcNow() => _utcNow;

        /// <summary>推进测试时间而不进行真实等待。</summary>
        public void Advance(TimeSpan elapsed) => _utcNow += elapsed;
    }
}
