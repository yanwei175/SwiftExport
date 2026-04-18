using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace MateWpfThemes.FW.Converters.Internal
{
    /// <summary>
    /// 中文注释：计算附加 Header 内容区域的左侧边距。
    /// </summary>
    public class TabControlAdditionalHeaderPanelContentMarginConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values != null && values.Length >= 4 &&
                values[0] is double &&
                values[1] is double &&
                values[2] is double &&
                values[3] is StackPanel)
            {
                double scrollableContentWidthDefault = (double)values[0];
                double scrollableContentWidthNonUniform = (double)values[1];
                double controlWidth = (double)values[2];
                var navPanel = (StackPanel)values[3];

                double scrollableContentWidth = Math.Max(scrollableContentWidthDefault, scrollableContentWidthNonUniform);
                double xOffset = Math.Min(controlWidth, scrollableContentWidth + navPanel.ActualWidth) - navPanel.ActualWidth;
                return new Thickness(xOffset, 0, 0, 0);
            }

            return new Thickness(0);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
