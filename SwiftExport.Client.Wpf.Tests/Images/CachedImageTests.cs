using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExportClient.Resources.Controls;
using SwiftExportClient.Services.Images;
using System.Windows.Controls;

namespace SwiftExport.Client.Wpf.Tests.Images;

[TestClass]
public sealed class CachedImageTests
{
    /// <summary>托管列表只请求小图，失败不会回退原图；打开 Tooltip 才请求原图。</summary>
    [TestMethod]
    public void ThumbnailFailure_ShouldKeepPlaceholderAndLoadOriginalOnlyOnTooltipOpen()
    {
        StaTest.RunAsync(async () =>
        {
            using var fixture = new SystemParameterClientFixture();
            using var resolver = new ImageAddressResolver(fixture.Cache);
            var service = new RecordingService { FailThumbnails = true };
            var resources = new ResourceDictionary { Source = new Uri("/SwiftExport.Client.Wpf;component/Resources/Image.xaml", UriKind.Relative) };
            var image = new CachedImage { CacheService = service, AddressResolver = resolver,
                SourcePath = "https://example.test/files/ProductImages/a.jpg", DecodePixelWidth = 800,
                Style = (Style)resources["SwiftExportCachedImage"] };
            var window = new Window { Width = 150, Height = 150, ShowInTaskbar = false, Content = image };
            window.Resources.MergedDictionaries.Add(resources);
            try
            {
                window.Show();
                await Task.Delay(30);
                Assert.HasCount(1, service.Requests);
                Assert.AreEqual("https://example.test/files/ProductImages/Thumbnails/a.jpg", service.Requests[0].SourcePath);
                Assert.AreEqual(256, service.Requests[0].DecodePixelWidth);
                Assert.IsTrue(image.LoadFailed);
                Assert.IsNotNull(image.Source);
                var tooltip = (ToolTip)image.ToolTip;
                var original = (CachedImage)((Border)tooltip.Content).Child;
                original.CacheService = service; original.AddressResolver = resolver;
                tooltip.PlacementTarget = image;
                tooltip.IsOpen = true;
                await Task.Delay(40);
                Assert.HasCount(2, service.Requests);
                Assert.AreEqual(image.SourcePath, service.Requests[1].SourcePath);
                Assert.AreEqual(400, service.Requests[1].DecodePixelWidth);
                tooltip.IsOpen = false;
            }
            finally { window.Close(); }
        });
    }

    /// <summary>同一个引用在参数加载成功后重新解析；卸载释放配置订阅和图像引用。</summary>
    [TestMethod]
    public void ConfigurationChanged_ShouldReloadVisibleImageAndDetachOnUnload()
    {
        StaTest.RunAsync(async () =>
        {
            using var fixture = new SystemParameterClientFixture(null);
            using var resolver = new ImageAddressResolver(fixture.Cache);
            var service = new RecordingService();
            var image = new CachedImage { SourcePath = "https://example.test/files/ProductImages/a.jpg",
                DisplayMode = ImageDisplayMode.Thumbnail, CacheService = service, AddressResolver = resolver };
            var window = new Window { Width = 150, Height = 150, Content = image, ShowInTaskbar = false };
            window.Show();
            await Task.Delay(20);
            Assert.HasCount(0, service.Requests);
            fixture.Cache.ApplySaved(SystemParameterClientFixture.Parameter("https://example.test/files/ProductImages/"));
            await Task.Delay(20);
            Assert.HasCount(1, service.Requests);
            var unloaded = new TaskCompletionSource();
            image.Unloaded += (_, _) => unloaded.SetResult();
            window.Close();
            await unloaded.Task.WaitAsync(TimeSpan.FromSeconds(1));
            fixture.Cache.ApplySaved(SystemParameterClientFixture.Parameter("https://example.test/new/", 2));
            await Task.Delay(20);
            Assert.HasCount(1, service.Requests);
            Assert.IsNull(image.Source);
        });
    }

    [TestMethod]
    public void SourcePathRapidChange_ShouldOnlyDisplayLatestRequest()
    {
        StaTest.RunAsync(async () =>
        {
            var first = CreateBitmap(Colors.Red);
            var second = CreateBitmap(Colors.Blue);
            var service = new DelayedImageCacheService(first, second);
            var image = new CachedImage { CacheService = service, SourcePath = "first" };
            var unloaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            image.Unloaded += (_, _) => unloaded.TrySetResult();
            var window = new Window
            {
                Width = 120,
                Height = 120,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None,
                Content = image
            };

            try
            {
                window.Show();
                await Task.Delay(20);
                image.SourcePath = "second";
                await Task.Delay(180);

                Assert.AreSame(second, image.Source);
                Assert.IsFalse(image.IsLoading);
                Assert.IsFalse(image.LoadFailed);
            }
            finally
            {
                window.Close();
            }

            await unloaded.Task.WaitAsync(TimeSpan.FromSeconds(1));
            Assert.IsNull(image.Source, "Unloaded 后应清除 Source，避免虚拟化回收时残留旧图。");
        });
    }

    /// <summary>记录实际请求，失败测试仍使用真实 WPF 控件生命周期。</summary>
    private sealed class RecordingService : IImageCacheService
    {
        public bool FailThumbnails { get; set; }
        public List<ImageLoadAddress> Requests { get; } = [];
        /// <inheritdoc />
        public ValueTask<BitmapSource?> GetAsync(string sourcePath, int decodePixelWidth, CancellationToken cancellationToken = default)
        {
            Requests.Add(new(sourcePath, decodePixelWidth));
            return ValueTask.FromResult(FailThumbnails && sourcePath.Contains("/Thumbnails/") ? null : CreateBitmap(Colors.Blue));
        }
        /// <inheritdoc />
        public ImageCacheStatistics GetStatistics() => new(0, 0, 0, 0, 0, 0, 0);
        /// <inheritdoc />
        public Task ClearAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private static BitmapSource CreateBitmap(Color color)
    {
        var pixels = new[] { color.B, color.G, color.R, color.A };
        var bitmap = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, pixels, 4);
        bitmap.Freeze();
        return bitmap;
    }

    private sealed class DelayedImageCacheService(BitmapSource first, BitmapSource second) : IImageCacheService
    {
        public async ValueTask<BitmapSource?> GetAsync(
            string sourcePath,
            int decodePixelWidth,
            CancellationToken cancellationToken = default)
        {
            // 故意不响应第一个请求的取消，用来验证控件自身的版本门闩。
            if (sourcePath == "first")
            {
                await Task.Delay(120);
                return first;
            }

            await Task.Delay(10, cancellationToken);
            return second;
        }

        public ImageCacheStatistics GetStatistics() => new(0, 0, 0, 0, 0, 0, 0);

        public Task ClearAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
