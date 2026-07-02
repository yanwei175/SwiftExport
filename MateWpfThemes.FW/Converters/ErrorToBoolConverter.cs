using System;
using System.Globalization;
using System.Windows.Data;

namespace MateWpfThemes.FW.Converters
{
    public class ErrorToBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // 如果返回的错误文本不为空，说明有错误，返回 True 触发红框
            return !string.IsNullOrWhiteSpace(value as string);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }
}
