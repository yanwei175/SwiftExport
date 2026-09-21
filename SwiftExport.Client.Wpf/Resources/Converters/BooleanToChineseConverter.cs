using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SwiftExportClient.Resources.Converters
{
    public sealed class BooleanToChineseConverter : IValueConverter
    {
        public object Convert(
            object value,
            Type targetType,
            object parameter,
            CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                return boolValue ? "是" : "否";
            }

            if (value == null)
            {
                return "-";
            }

            return string.Empty;
        }

        public object ConvertBack(
            object value,
            Type targetType,
            object parameter,
            CultureInfo culture)
        {
            if (value is string text)
            {
                text = text.Trim();

                if (text == "是")
                    return true;

                if (text == "否")
                    return false;

                if (text == "-")
                {
                    // 只有目标是 bool? 时，null 才合理
                    if (Nullable.GetUnderlyingType(targetType) == typeof(bool))
                        return null!;

                    return DependencyProperty.UnsetValue;
                }
            }

            return Binding.DoNothing;
        }
    }
}