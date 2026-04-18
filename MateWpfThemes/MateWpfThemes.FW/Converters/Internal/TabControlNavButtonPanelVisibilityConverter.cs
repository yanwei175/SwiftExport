using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MateWpfThemes.FW.Converters.Internal
{
    /// <summary>
    /// 中文注释：当启用导航面板且页签溢出时显示导航按钮面板。
    /// </summary>
    public class TabControlNavButtonPanelVisibilityConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values != null && values.Length >= 2 && values[0] is bool && values[1] is bool)
            {
                bool useNavigationPanel = (bool)values[0];
                bool isOverflowing = (bool)values[1];
                return useNavigationPanel && isOverflowing ? Visibility.Visible : Visibility.Collapsed;
            }

            return Visibility.Collapsed;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
