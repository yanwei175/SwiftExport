using System.Globalization;
using System.Windows.Data;

namespace SwiftExportClient.Resources.Converters
{
    public class IsIntegerConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null)
                return true;

            if (value is double d)
                return Math.Abs(d % 1) < 0.0000001;

            if (value is decimal m)
                return m % 1 == 0;

            if (value is float f)
                return Math.Abs(f % 1) < 0.0000001f;

            return true;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }
}
