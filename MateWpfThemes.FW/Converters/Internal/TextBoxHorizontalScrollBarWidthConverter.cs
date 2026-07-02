using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MateWpfThemes.FW.Converters.Internal
{
    /// <summary>
    /// 中文注释：根据纵向滚动条可见性计算横向滚动条可用宽度。
    /// </summary>
    public class TextBoxHorizontalScrollBarWidthConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values != null && values.Length >= 2 && values[0] is double && values[1] is Visibility)
            {
                double contentHostWidth = (double)values[0];
                Visibility verticalScrollBarVisibility = (Visibility)values[1];
                return Math.Max(0, contentHostWidth - (verticalScrollBarVisibility == Visibility.Visible ? SystemParameters.VerticalScrollBarWidth : 0));
            }

            return double.NaN;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
