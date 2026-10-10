using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace SwiftExport.Client.VSTO.Resources.Controls
{
    /// <summary>读取匿名 HTTPS 或本地文件并后台解码，缓存冻结缩略图；连接与缓存仅属于本插件。</summary>
    internal sealed class ImageSourceLoader : IDisposable
    {
        private const int MaxImageBytes = 16 * 1024 * 1024;
        private const long MaxCacheBytes = 32 * 1024 * 1024;
        private static readonly object sharedGate = new object();
        private static ImageSourceLoader shared;
        private readonly HttpClient client;
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private readonly SemaphoreSlim concurrency = new SemaphoreSlim(4);
        private readonly object cacheGate = new object();
        private readonly Dictionary<string, CacheEntry> cache = new Dictionary<string, CacheEntry>();
        private readonly Queue<string> insertionOrder = new Queue<string>();
        private long cacheBytes;
        private bool disposed;

        /// <summary>延迟创建共享读取器，仅首次显示有图片的控件时建立连接。</summary>
        internal static ImageSourceLoader Shared
        {
            get { lock (sharedGate) return shared ?? (shared = new ImageSourceLoader(CreateTransport())); }
        }

        /// <summary>只为图片连接固定 TLS 1.2，保留证书验证，不发送 Cookie 或跟随未知重定向。</summary>
        internal static HttpClientHandler CreateTransport() => new HttpClientHandler
        { SslProtocols = SslProtocols.Tls12, UseCookies = false, AllowAutoRedirect = false };

        /// <summary>接收独占传输，测试可替换响应，不使用 API 认证处理器。</summary>
        internal ImageSourceLoader(HttpMessageHandler transport)
        {
            client = new HttpClient(transport) { Timeout = TimeSpan.FromSeconds(20) };
        }

        /// <summary>插件停止时取消共享读取、释放 HTTP 连接并清空内存，不影响 Excel 的其他插件。</summary>
        internal static void ReleaseShared()
        {
            lock (sharedGate) { shared?.Dispose(); shared = null; }
        }

        /// <summary>接受 HTTPS 和本地绝对路径／file URI，拒绝相对路径、UNC、HTTP 及内嵌凭据。</summary>
        internal static bool TryGetUri(string path, out Uri uri)
        {
            return Uri.TryCreate(path?.Trim(), UriKind.Absolute, out uri) && !uri.IsUnc
                && string.IsNullOrEmpty(uri.UserInfo)
                && ((uri.Scheme == Uri.UriSchemeHttps && !string.IsNullOrEmpty(uri.Host))
                    || (uri.IsFile && Path.IsPathRooted(uri.LocalPath)));
        }

        /// <summary>限制读取并发，后台读取／解码；缓存键包含路径、尺寸及本地文件版本。</summary>
        internal async Task<BitmapSource> LoadAsync(Uri uri, int width, CancellationToken cancellation)
        {
            using (var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellation, lifetime.Token))
            {
                // ResponseHeadersRead 后正文也使用同一超时，防止服务器发送头后长期不返回图片。
                operation.CancelAfter(TimeSpan.FromSeconds(20));
                var token = operation.Token;
                await concurrency.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    return await Task.Run(async () =>
                    {
                        token.ThrowIfCancellationRequested();
                        var key = uri.AbsoluteUri + "|" + width;
                        if (uri.IsFile)
                        {
                            var info = new FileInfo(uri.LocalPath);
                            key += "|" + info.LastWriteTimeUtc.Ticks + "|" + info.Length;
                        }
                        lock (cacheGate)
                        {
                            CacheEntry hit;
                            if (disposed) throw new ObjectDisposedException(nameof(ImageSourceLoader));
                            if (cache.TryGetValue(key, out hit))
                            {
                                if (hit.Expires > DateTime.UtcNow) return hit.Image;
                                // 过期项目移出容量和队列，后续成功读取可以重新缓存。
                                cacheBytes -= hit.Bytes;
                                cache.Remove(key);
                                var count = insertionOrder.Count;
                                while (count-- > 0) { var item = insertionOrder.Dequeue(); if (item != key) insertionOrder.Enqueue(item); }
                            }
                        }
                        byte[] bytes;
                        if (uri.IsFile)
                        {
                            using (var stream = new FileStream(uri.LocalPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                                bytes = await ReadBytesAsync(stream, token).ConfigureAwait(false);
                        }
                        else
                        {
                            using (var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false))
                            {
                                response.EnsureSuccessStatusCode();
                                using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                                    bytes = await ReadBytesAsync(stream, token).ConfigureAwait(false);
                            }
                        }
                        token.ThrowIfCancellationRequested();
                        var image = new BitmapImage();
                        using (var stream = new MemoryStream(bytes, false))
                        {
                            image.BeginInit();
                            image.CacheOption = BitmapCacheOption.OnLoad;
                            image.DecodePixelWidth = width;
                            image.StreamSource = stream;
                            image.EndInit();
                            image.Freeze();
                        }
                        token.ThrowIfCancellationRequested();
                        AddCache(key, image);
                        return (BitmapSource)image;
                    }, token).ConfigureAwait(false);
                }
                finally { concurrency.Release(); }
            }
        }

        /// <summary>按流分段读取，限制单图 16 MiB，不写临时文件并允许取消。</summary>
        private static async Task<byte[]> ReadBytesAsync(Stream stream, CancellationToken token)
        {
            using (var output = new MemoryStream())
            {
                var buffer = new byte[8192];
                int count;
                while ((count = await stream.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false)) != 0)
                {
                    if (output.Length + count > MaxImageBytes) throw new IOException("图片超过读取上限。");
                    output.Write(buffer, 0, count);
                }
                return output.ToArray();
            }
        }

        /// <summary>最多保留 128 个缩略图及估算 32 MiB 像素数据，五分钟过期，失败不缓存。</summary>
        private void AddCache(string key, BitmapSource image)
        {
            var bytes = (long)image.PixelWidth * image.PixelHeight * Math.Max(4, (image.Format.BitsPerPixel + 7) / 8);
            lock (cacheGate)
            {
                if (disposed || bytes > MaxCacheBytes || cache.ContainsKey(key)) return;
                while (cache.Count >= 128 || cacheBytes + bytes > MaxCacheBytes)
                {
                    var oldest = insertionOrder.Dequeue();
                    cacheBytes -= cache[oldest].Bytes;
                    cache.Remove(oldest);
                }
                cache[key] = new CacheEntry { Image = image, Bytes = bytes, Expires = DateTime.UtcNow.AddMinutes(5) };
                insertionOrder.Enqueue(key);
                cacheBytes += bytes;
            }
        }

        /// <summary>幂等关闭读取器；取消中的操作自行释放令牌和并发许可。</summary>
        public void Dispose()
        {
            lock (cacheGate)
            {
                if (disposed) return;
                disposed = true;
                cache.Clear(); insertionOrder.Clear(); cacheBytes = 0;
            }
            lifetime.Cancel();
            client.Dispose();
        }

        /// <summary>保存冻结位图及缓存容量、过期时间，不保留原始响应或认证信息。</summary>
        private sealed class CacheEntry
        {
            internal BitmapSource Image;
            internal long Bytes;
            internal DateTime Expires;
        }
    }
}
