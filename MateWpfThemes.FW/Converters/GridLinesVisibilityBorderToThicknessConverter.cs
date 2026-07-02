using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace MateWpfThemes.FW.Converters
{
    public class GridLinesVisibilityBorderToThicknessConverter : IValueConverter
    {
        public static readonly GridLinesVisibilityBorderToThicknessConverter Instance = new GridLinesVisibilityBorderToThicknessConverter();

        private const double GridLinesThickness = 1;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (!(value is DataGridGridLinesVisibility))
                return Binding.DoNothing;

            var visibility = (DataGridGridLinesVisibility)value;

            double thickness = GridLinesThickness;
            if (parameter is double pd)
            {
                thickness = pd;
            }
            else if (parameter != null)
            {
                double parsed;
                if (double.TryParse(parameter.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out parsed))
                    thickness = parsed;
            }

            switch (visibility)
            {
                case DataGridGridLinesVisibility.All:
                    return new Thickness(0, 0, thickness, thickness);
                case DataGridGridLinesVisibility.Horizontal:
                    return new Thickness(0, 0, 0, thickness);
                case DataGridGridLinesVisibility.Vertical:
                    return new Thickness(0, 0, thickness, 0);
                case DataGridGridLinesVisibility.None:
                    return new Thickness(0);
                default:
                    throw new ArgumentOutOfRangeException("value", "Unexpected DataGridGridLinesVisibility value");
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }

    }
}
