using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SwiftExportClient.Resources.Converters;

/// <summary>将原生可见状态与菜单勾选状态双向转换；取消勾选折叠目标。</summary>
public sealed class VisibilityToBooleanConverter : IValueConverter
{
    /// <summary>仅 Visible 显示为已勾选。</summary>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is Visibility.Visible;

    /// <summary>将菜单勾选状态还原为 Visible 或 Collapsed。</summary>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Visible : Visibility.Collapsed;
}
