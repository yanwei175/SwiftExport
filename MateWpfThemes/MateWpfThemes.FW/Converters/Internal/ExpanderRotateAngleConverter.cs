using System;
using System.Globalization;
using System.Windows.Controls;
using System.Windows.Data;

namespace MateWpfThemes.FW.Converters.Internal
{
    /// <summary>
    /// 中文注释：根据 ExpandDirection 计算箭头旋转角度。
    /// </summary>
    public class ExpanderRotateAngleConverter : IValueConverter
    {
        public static readonly ExpanderRotateAngleConverter Instance = new ExpanderRotateAngleConverter();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            double factor = 1.0;
            if (parameter != null)
            {
                if (!double.TryParse(parameter.ToString(), out factor))
                {
                    factor = 1.0;
                }
            }

            if (value is ExpandDirection)
            {
                var direction = (ExpandDirection)value;
                switch (direction)
                {
                    case ExpandDirection.Left:
                        return 90 * factor;
                    case ExpandDirection.Right:
                        return -90 * factor;
                    default:
                        return 0;
                }
            }

            return 0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
