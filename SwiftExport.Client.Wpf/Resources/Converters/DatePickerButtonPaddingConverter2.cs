using System.Globalization;
using System.Windows;

namespace SwiftExportClient.Resources.Converters
{
    internal class DatePickerButtonPaddingConverter2
    {    /// <inheritdoc />
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values is [Thickness padding, Thickness buttonMargin, double buttonWidth])
            {
                return new Thickness(
                    padding.Left,
                    padding.Top,
                    padding.Right + buttonMargin.Left + buttonMargin.Right + buttonWidth,
                    padding.Bottom
                );
            }

            return default(Thickness);
        }

        /// <inheritdoc />
        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
