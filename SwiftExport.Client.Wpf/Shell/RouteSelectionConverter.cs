using System.Globalization;
using System.Windows.Data;

namespace SwiftExportClient.Shell;

public sealed class RouteSelectionConverter : IMultiValueConverter
{
    public object Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[0] is not ShellNavigationItem item)
        {
            return false;
        }

        var selectedRouteKey = values[1]?.ToString();
        if (string.IsNullOrWhiteSpace(selectedRouteKey))
        {
            return false;
        }

        if (string.Equals(item.RouteKey, selectedRouteKey, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var isPaneOpen = values.Length < 3 || values[2] is not bool paneOpen || paneOpen;
        return !isPaneOpen && item.Children.Any(child => ContainsRoute(child, selectedRouteKey));
    }

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }

    private static bool ContainsRoute(ShellNavigationItem item, string routeKey)
    {
        return string.Equals(item.RouteKey, routeKey, StringComparison.OrdinalIgnoreCase)
               || item.Children.Any(child => ContainsRoute(child, routeKey));
    }
}
