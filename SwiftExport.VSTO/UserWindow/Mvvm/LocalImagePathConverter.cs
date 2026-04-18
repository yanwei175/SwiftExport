using System;
using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace SwiftExport.VSTO.UserWindow.Mvvm
{
    public class LocalImagePathConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string path = value as string;

            if (string.IsNullOrWhiteSpace(path)) return null;

            try
            {
                if (!File.Exists(path)) return null;

                var bitmap = new BitmapImage();
                bitmap.BeginInit();

                // 1. 立即加载，释放文件锁
                bitmap.CacheOption = BitmapCacheOption.OnLoad;

                // 2. 指定源
                bitmap.UriSource = new Uri(path, UriKind.Absolute);

                // 3. ★ 核心优化：只解码 100 像素宽度的缩略图 ★
                // 你的列表里图片显示只有 28x28，这里设 60-100 足够清晰了，且内存极小
                bitmap.DecodePixelWidth = 400;

                bitmap.EndInit();
                bitmap.Freeze(); // 冻结，高性能关键

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