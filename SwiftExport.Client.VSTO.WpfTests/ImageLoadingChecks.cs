using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SwiftExport.Client.VSTO.Resources.Controls;

namespace SwiftExport.Client.VSTO.WpfTests
{
    /// <summary>真实解码图片并验证 HTTPS／本地读取、内存复用、取消及过期响应，不访问真实 API。</summary>
    internal static class ImageLoadingChecks
    {
        /// <summary>使用可控 HTTP 响应和测试临时图片检查真实控件生命周期。</summary>
        internal static void Run(Action<bool, string> check)
        {
            using (var transport = ImageSourceLoader.CreateTransport())
                check(transport.SslProtocols == SslProtocols.Tls12 && !transport.UseCookies && !transport.AllowAutoRedirect,
                    "图片连接显式 TLS 1.2，保留证书验证且隔离 Cookie／重定向");
            var handler = new Transport();
            using (var loader = new ImageSourceLoader(handler))
            {
                var image = new HttpsImage(loader) { SourcePath = "https://vsto-image.test/first.png", DecodePixelWidth = 8 };
                check(handler.Count == 0 && image.Source == null, "未加载的图片不预取");
                var window = new Window { Content = image, Width = 120, Height = 120, Left = -10000, ShowActivated = false, ShowInTaskbar = false };
                window.Show(); Pump();
                var localPath = Path.Combine(Path.GetTempPath(), "vsto-image-" + Guid.NewGuid().ToString("N") + ".png");
                try
                {
                    WaitUntil(() => handler.Count == 1);
                    check(image.IsLoading, "下载中状态可供绑定");
                    handler.Complete(0, Png()); WaitUntil(() => !image.IsLoading);
                    var first = image.Source;
                    check(!image.LoadFailed && ((BitmapSource)first).PixelWidth == 8 && first.IsFrozen,
                        "真实 PNG 解码完成并冻结缩略图");
                    check(handler.NoCredentials, "图片请求没有附加令牌或 Cookie");
                    image.SourcePath = null; image.SourcePath = "https://vsto-image.test/first.png";
                    WaitUntil(() => !image.IsLoading);
                    check(handler.Count == 1 && ReferenceEquals(first, image.Source), "相同路径及尺寸命中内存，不重复下载");
                    image.DecodePixelWidth = 12; WaitUntil(() => handler.Count == 2);
                    handler.Complete(1, Png()); WaitUntil(() => !image.IsLoading);
                    check(((BitmapSource)image.Source).PixelWidth == 12, "不同解码尺寸分别缓存");
                    image.SourcePath = "https://vsto-image.test/old.png"; WaitUntil(() => handler.Count == 3);
                    image.SourcePath = "https://vsto-image.test/new.png"; WaitUntil(() => handler.Count == 4);
                    handler.Complete(3, Png()); WaitUntil(() => !image.IsLoading);
                    var newest = image.Source;
                    handler.Complete(2, Png()); Pump();
                    check(ReferenceEquals(newest, image.Source) && handler.WasCancelled(2), "换址取消旧请求，旧响应不能覆盖新图");
                    image.SourcePath = "https://vsto-image.test/corrupt.png"; WaitUntil(() => handler.Count == 5);
                    handler.Complete(4, new byte[] { 1, 2, 3 }); WaitUntil(() => image.LoadFailed);
                    check(image.Source == null && !image.IsLoading, "损坏图片正常留白并报告失败");
                    image.SourcePath = null; image.SourcePath = "https://vsto-image.test/corrupt.png";
                    WaitUntil(() => handler.Count == 6); handler.Complete(5, Png()); WaitUntil(() => !image.IsLoading);
                    check(!image.LoadFailed && image.Source != null, "失败不缓存，再次加载可恢复");
                    image.SourcePath = "https://vsto-image.test/unloaded.png"; WaitUntil(() => handler.Count == 7);
                    window.Content = null; Pump(); handler.Complete(6, Png()); Pump();
                    check(image.Source == null && !image.IsLoading && handler.WasCancelled(6), "卸载取消下载并阻止迟到回写");
                    image.SourcePath = null; window.Content = image; Pump();
                    image.SourcePath = "https://vsto-image.test/fail.png"; WaitUntil(() => handler.Count == 8);
                    handler.Fail(7); WaitUntil(() => image.LoadFailed);
                    check(image.Source == null, "网络失败不会残留其他产品图片");
                    File.WriteAllBytes(localPath, Png()); image.SourcePath = localPath; WaitUntil(() => !image.IsLoading);
                    var local = image.Source;
                    check(!image.LoadFailed && local != null, "本地磁盘绝对路径可以显示");
                    image.SourcePath = new Uri(localPath).AbsoluteUri; WaitUntil(() => !image.IsLoading);
                    check(ReferenceEquals(local, image.Source), "file URI 与绝对路径复用同一内存图片");
                    File.WriteAllBytes(localPath, Png()); File.SetLastWriteTimeUtc(localPath, DateTime.UtcNow.AddSeconds(5));
                    image.SourcePath = null; image.SourcePath = localPath; WaitUntil(() => !image.IsLoading);
                    check(image.Source != null && !ReferenceEquals(local, image.Source), "本地文件版本变化不复用旧缩略图");
                    File.Delete(localPath); image.SourcePath = null; image.SourcePath = localPath;
                    WaitUntil(() => image.LoadFailed);
                    check(image.Source == null, "不存在的本地文件正常报告失败");
                    foreach (var path in new[] { "http://vsto-image.test/plain.png", "/relative.png", "relative.png", "\\\\server\\share\\image.png", "https://user:password@vsto-image.test/a.png" })
                    {
                        image.SourcePath = path;
                        check(image.LoadFailed && image.Source == null, "拒绝 HTTP、相对路径、UNC 或内嵌凭据");
                    }
                    check(handler.Count == 8, "本地与非法路径不进入网络传输");
                    image.SourcePath = null;
                    check(!image.LoadFailed && !image.IsLoading && image.Source == null, "空图片正常留白");
                    image.SourcePath = "https://vsto-image.test/dispose.png"; WaitUntil(() => handler.Count == 9);
                    loader.Dispose(); handler.Complete(8, Png()); WaitUntil(() => !image.IsLoading);
                    check(image.Source == null && handler.WasCancelled(8), "读取器释放时取消所有活动请求");
                }
                finally { window.Close(); Pump(); if (File.Exists(localPath)) File.Delete(localPath); }
                check(handler.Disposed, "读取器释放独占 HTTP 资源");
            }
            CheckCapacity(check);
        }

        /// <summary>验证第 129 个地址触发内存条目淘汰，不形成无限增长的缓存。</summary>
        private static void CheckCapacity(Action<bool, string> check)
        {
            using (var handler = new ImmediateTransport())
            using (var loader = new ImageSourceLoader(handler))
            {
                for (var i = 0; i < 129; i++) loader.LoadAsync(new Uri("https://cache.test/" + i), 8, CancellationToken.None).GetAwaiter().GetResult();
                loader.LoadAsync(new Uri("https://cache.test/128"), 8, CancellationToken.None).GetAwaiter().GetResult();
                check(handler.Count == 129, "缓存中的新图片复用");
                loader.LoadAsync(new Uri("https://cache.test/0"), 8, CancellationToken.None).GetAwaiter().GetResult();
                check(handler.Count == 130, "缓存容量达到上限时淘汰最早图片");
            }
        }

        /// <summary>可选实网诊断：模拟 Excel 旧全局 TLS 值，用生产控件读取指定图片并验证不污染全局。</summary>
        internal static void CheckServer(string address, Action<bool, string> check)
        {
            var original = ServicePointManager.SecurityProtocol;
            var old = SecurityProtocolType.Tls;
            ServicePointManager.SecurityProtocol = old;
            var image = new HttpsImage { SourcePath = address };
            var window = new Window { Content = image, Width = 120, Height = 120, Left = -10000, ShowInTaskbar = false, ShowActivated = false };
            try
            {
                window.Show(); Pump(); WaitUntil(() => !image.IsLoading, 25000);
                check(!image.LoadFailed && image.Source is BitmapSource, "实际服务器图片成功下载及解码");
                check(ServicePointManager.SecurityProtocol == old, "图片读取不修改 Excel 宿主全局 TLS");
                Console.WriteLine("Image decoded: " + ((BitmapSource)image.Source).PixelWidth + " x " + ((BitmapSource)image.Source).PixelHeight);
            }
            finally { window.Close(); Pump(); ImageSourceLoader.ReleaseShared(); ServicePointManager.SecurityProtocol = original; }
        }

        /// <summary>创建合法 PNG 字节供真实 WIC 解码，不保存响应缓存文件。</summary>
        private static byte[] Png()
        {
            var pixels = new byte[16 * 16 * 4];
            for (var i = 0; i < pixels.Length; i += 4) { pixels[i + 1] = 128; pixels[i + 3] = 255; }
            var source = BitmapSource.Create(16, 16, 96, 96, PixelFormats.Bgra32, null, pixels, 64);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(source));
            using (var stream = new MemoryStream()) { encoder.Save(stream); return stream.ToArray(); }
        }

        /// <summary>有界等待消息循环中的异步绑定与加载状态。</summary>
        private static void WaitUntil(Func<bool> condition, int milliseconds = 5000)
        {
            var watch = Stopwatch.StartNew();
            while (!condition()) { if (watch.ElapsedMilliseconds > milliseconds) throw new InvalidOperationException("图片测试等待超时。"); Pump(); Thread.Sleep(10); }
            Pump();
        }

        /// <summary>推进真实控件 Dispatcher。</summary>
        private static void Pump()
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
        }

        /// <summary>只接管独占测试读取器，可故意返回已经取消请求的迟到响应。</summary>
        private sealed class Transport : HttpMessageHandler
        {
            private readonly List<TaskCompletionSource<HttpResponseMessage>> pending = new List<TaskCompletionSource<HttpResponseMessage>>();
            private readonly List<CancellationToken> tokens = new List<CancellationToken>();
            internal int Count { get { lock (pending) return pending.Count; } }
            internal bool NoCredentials = true;
            internal bool Disposed;
            /// <summary>记录请求身份和取消令牌，不连接真实服务器。</summary>
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            {
                lock (pending)
                {
                    NoCredentials &= request.Headers.Authorization == null && !request.Headers.Contains("Cookie");
                    var completion = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
                    pending.Add(completion); tokens.Add(token); return completion.Task;
                }
            }
            internal void Complete(int index, byte[] bytes) { lock (pending) pending[index].SetResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }); }
            internal void Fail(int index) { lock (pending) pending[index].SetException(new HttpRequestException("模拟连接失败")); }
            internal bool WasCancelled(int index) { lock (pending) return tokens[index].IsCancellationRequested; }
            /// <summary>记录传输释放。</summary>
            protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
        }

        /// <summary>立即返回内存图片以验证缓存容量。</summary>
        private sealed class ImmediateTransport : HttpMessageHandler
        {
            private readonly byte[] bytes = Png();
            internal int Count;
            /// <summary>仅返回合法 PNG 字节，不访问网络。</summary>
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            {
                Count++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
            }
        }
    }
}
