using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MateWpfThemes.FW.Converters.Internal
{
    /// <summary>
    /// 中文注释：根据格式化字符串生成 Slider 的提示文本。
    /// </summary>
    public class SliderToolTipConverter : IMultiValueConverter
    {
        public static readonly SliderToolTipConverter Instance = new SliderToolTipConverter();

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values != null && values.Length >= 2 && values[1] is string)
            {
                var format = (string)values[1];
                if (!string.IsNullOrEmpty(format))
                {
                    try
                    {
                        return string.Format(culture, format, values[0]);
                    }
                    catch (FormatException)
                    {
                        // 中文注释：格式字符串异常时回退到默认转换逻辑。
                    }
                }
            }

            if (values != null && values.Length >= 1 && targetType != null)
            {
                return System.Convert.ChangeType(values[0], targetType, culture);
            }

            return DependencyProperty.UnsetValue;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
