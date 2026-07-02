using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MateWpfThemes.FW.Converters.Internal
{
    /// <summary>
    /// 中文注释：把 double 值转换为 CornerRadius，最小值为 0。
    /// </summary>
    public class DoubleToCornerRadiusConverter : IValueConverter
    {
        public static readonly DoubleToCornerRadiusConverter Instance = new DoubleToCornerRadiusConverter();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            double radius = value is double ? (double)value : 0d;
            return new CornerRadius(Math.Max(0, radius));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
