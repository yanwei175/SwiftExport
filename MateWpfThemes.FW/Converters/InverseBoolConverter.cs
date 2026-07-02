using System;
using System.Globalization;
using System.Windows.Data;

namespace MateWpfThemes.FW.Converters
{
    /// <summary>
    /// 布尔值反转转换器 (True变False，False变True)
    /// </summary>
    public class InverseBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b)
            {
                return !b;
            }
            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b)
            {
                return !b;
            }
            return false;
        }
    }
}
