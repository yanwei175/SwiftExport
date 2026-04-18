using System;
using System.Globalization;
using System.Windows.Controls;
using System.Windows.Data;

namespace MateWpfThemes.FW.Converters.Internal
{
    /// <summary>
    /// 中文注释：根据滑块方向计算值标签位置偏移。
    /// </summary>
    [ValueConversion(typeof(double), typeof(double), ParameterType = typeof(Orientation))]
    public class SliderValueLabelPositionConverter : IValueConverter
    {
        public static readonly SliderValueLabelPositionConverter Instance = new SliderValueLabelPositionConverter();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (parameter is Orientation && value is double)
            {
                var orientation = (Orientation)parameter;
                var width = (double)value;

                const double halfGripWidth = 9.0;
                const double margin = 4.0;

                switch (orientation)
                {
                    case Orientation.Horizontal:
                        return -width * 0.5 + halfGripWidth;
                    case Orientation.Vertical:
                        return -width - margin;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(parameter));
                }
            }

            return 0.0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
