using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MateWpfThemes.FW.Converters
{
    public class BoolToVisibilityConverter : IValueConverter
    {
        // 从 ViewModel (bool) 转换到 UI (Visibility)
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool isVisible = false;
            // 如果绑定的值是普通的 bool，或者是有值的 bool?，这里都会完美命中！
            // 如果绑定的值是 null，这里不会命中，isVisible 保持 false。
            if (value is bool boolValue)
            {
                isVisible = boolValue;
            }

            // 处理反转逻辑 (例如传入 ConverterParameter="Inverse")
            if (parameter != null && parameter.ToString().Equals("Inverse", StringComparison.OrdinalIgnoreCase))
            {
                isVisible = !isVisible;
            }

            return isVisible ? Visibility.Visible : Visibility.Collapsed;
        }

        // 从 UI (Visibility) 转换回 ViewModel (bool)
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Visibility visibility)
            {
                bool result = visibility == Visibility.Visible;

                if (parameter != null && parameter.ToString().Equals("Inverse", StringComparison.OrdinalIgnoreCase))
                {
                    return !result;
                }
                return result;
            }
            return false;
        }
    }
}
