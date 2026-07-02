using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MateWpfThemes.FW.Converters.Internal
{
    /// <summary>
    /// 中文注释：根据前后导航按钮可见性决定导航面板外边距。
    /// </summary>
    public class TabControlNavButtonPanelMarginConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values != null && values.Length >= 3 &&
                values[0] is Thickness &&
                values[1] is Visibility &&
                values[2] is Visibility)
            {
                var margin = (Thickness)values[0];
                var previousButtonVisibility = (Visibility)values[1];
                var nextButtonVisibility = (Visibility)values[2];

                return previousButtonVisibility == Visibility.Collapsed && nextButtonVisibility == Visibility.Collapsed
                    ? new Thickness(0)
                    : margin;
            }

            return new Thickness(0);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
