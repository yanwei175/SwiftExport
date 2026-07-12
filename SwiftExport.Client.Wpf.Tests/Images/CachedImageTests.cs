using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExportClient.Resources.Controls;
using SwiftExportClient.Services.Images;

namespace SwiftExport.Client.Wpf.Tests.Images;

[TestClass]
public sealed class CachedImageTests
{
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
