using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace MateWpfThemes.FW.Converters
{
    public class ImgPathToImgConverter : IValueConverter
    {
        private static readonly Dictionary<string, BitmapImage> _imageCache = new Dictionary<string, BitmapImage>(StringComparer.OrdinalIgnoreCase);
        private static readonly LinkedList<string> _accessQueue = new LinkedList<string>();
        private static readonly object _lock = new object();
        private const int MaxCacheSize = 1000; // 缓存最大图片数量限制

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string path = value as string;

            if (string.IsNullOrWhiteSpace(path)) return null;

            try
            {
                lock (_lock)
                {
                    if (_imageCache.TryGetValue(path, out var cachedImage))
                    {
                        // 命中缓存，更新为最近使用
                        _accessQueue.Remove(path);
                        _accessQueue.AddLast(path);
                        return cachedImage;
                    }
                }

                if (!File.Exists(path)) return null;

                var bitmap = new BitmapImage();
                bitmap.BeginInit();

                // 1. 立即加载，释放文件锁
                bitmap.CacheOption = BitmapCacheOption.OnLoad;

                // 2. 指定源
                bitmap.UriSource = new Uri(path, UriKind.Absolute);

                // 3. ★ 核心优化：只解码 100-400 像素宽度的缩略图 ★
                bitmap.DecodePixelWidth = 400;

                bitmap.EndInit();
                bitmap.Freeze(); // 冻结，高性能关键

                lock (_lock)
                {
                    if (!_imageCache.ContainsKey(path))
                    {
                        // 超过缓存限制时，移除最久未使用的图片 (LRU)
                        if (_imageCache.Count >= MaxCacheSize)
                        {
                            string oldest = _accessQueue.First.Value;
                            _accessQueue.RemoveFirst();
                            _imageCache.Remove(oldest);
                        }

                        _imageCache[path] = bitmap;
                        _accessQueue.AddLast(path);
                    }
                }

                return bitmap;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}