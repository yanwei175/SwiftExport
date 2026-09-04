using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SwiftExportClient.Resources.Converters;

/// <summary>
/// 空值转可见性转换器
/// </summary>
public class NullToVisibilityConverter : IValueConverter
{
    // 我们给它加两个属性，让它变得极其灵活
    // 默认行为：如果是 Null 就显示 (Visible)，如果不是 Null 就隐藏 (Collapsed)
    // 这样非常适合用来做“空状态提示 (Empty State)”
    public Visibility NullVisibility { get; set; } = Visibility.Visible;
    public Visibility NotNullVisibility { get; set; } = Visibility.Collapsed;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value == null ? NullVisibility : NotNullVisibility;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
