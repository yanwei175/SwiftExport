using SwiftExport.UI.Interfaces;
using System.Collections.Concurrent;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SwiftExport.UI.Common
{
    public class CachedImageProvider : IImageProvider
    {
        // 线程安全的内存缓存
        private readonly ConcurrentDictionary<string, ImageSource> _cache
            = new();

        public async Task<ImageSource> GetImageAsync(string pathOrUrl)
        {
            // 1️⃣ 先查缓存
            if (_cache.TryGetValue(pathOrUrl, out var cached))
                return cached;

            // 2️⃣ 缓存未命中，异步加载
            var image = await Task.Run(() =>
            {
                // 创建 BitmapImage
                var bitmap = new BitmapImage();

                bitmap.BeginInit();

                // 既支持本地路径，也支持 http(s)
                bitmap.UriSource = new Uri(pathOrUrl, UriKind.RelativeOrAbsolute);

                // 强制一次性加载到内存，避免文件锁
                bitmap.CacheOption = BitmapCacheOption.OnLoad;

                bitmap.EndInit();

                // 冻结对象，允许跨线程使用
                bitmap.Freeze();

                return (ImageSource)bitmap;
            });

            // 3️⃣ 写入缓存
            _cache[pathOrUrl] = image;

            return image;
        }
    }
}
