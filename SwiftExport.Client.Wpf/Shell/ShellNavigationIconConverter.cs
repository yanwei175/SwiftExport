using System.Globalization;
using System.Windows.Data;
using Wpf.Ui.Controls;

namespace SwiftExportClient.Shell;

public sealed class ShellNavigationIconConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var icon = value?.ToString();
        if (!string.IsNullOrWhiteSpace(icon) &&
            Enum.TryParse<SymbolRegular>(icon, ignoreCase: true, out var symbol))
        {
            return symbol;
        }

        return SymbolRegular.Document24;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
