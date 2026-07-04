using System.Globalization;
using System.Windows.Data;

namespace SwiftExportClient.Shell;

public sealed class RouteSelectionConverter : IMultiValueConverter
{
    public object Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Length < 2)
        {
            return false;
        }

        var routeKey = values[0]?.ToString();
        var selectedRouteKey = values[1]?.ToString();

        return !string.IsNullOrWhiteSpace(routeKey) &&
               string.Equals(routeKey, selectedRouteKey, StringComparison.OrdinalIgnoreCase);
    }

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
