using System.Globalization;
using System.Windows;
using System.Windows.Data;
using Wpf.Ui.Controls;

namespace SwiftExportClient.Shell;

/// <summary>
/// 在 View 层把导航数据转换为 WPF-UI 所需的真实导航控件，避免 ViewModel 依赖控件类型。
/// </summary>
[ValueConversion(typeof(object[]), typeof(IReadOnlyList<NavigationViewItem>))]
public sealed class ShellNavigationItemsConverter : IMultiValueConverter
{
    private static readonly RouteSelectionConverter SelectionConverter = new();

    /// <summary>
    /// 将导航模型和 Shell 数据上下文转换为 WPF-UI 导航项，生成项直接绑定 Shell，避免依赖尚未建立的窗口祖先链。
    /// </summary>
    public object Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        return values.Length >= 2
            && values[0] is IEnumerable<ShellNavigationItem> items
            && values[1] is { } shellDataContext
            && shellDataContext != DependencyProperty.UnsetValue
            ? CreateItems(items, shellDataContext)
            : Array.Empty<NavigationViewItem>();
    }

    /// <summary>
    /// 导航控件集合不承担反向写回菜单模型的职责。
    /// </summary>
    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }

    private static IReadOnlyList<NavigationViewItem> CreateItems(
        IEnumerable<ShellNavigationItem> items,
        object shellDataContext)
    {
        return items.Select(item => CreateItem(item, shellDataContext)).ToArray();
    }

    private static NavigationViewItem CreateItem(ShellNavigationItem model, object shellDataContext)
    {
        var item = new NavigationViewItem
        {
            DataContext = model,
            Content = model.Title,
            Icon = new SymbolIcon(ParseSymbol(model.Icon)),
            MenuItemsSource = CreateItems(model.Children, shellDataContext),
        };

        item.SetBinding(
            NavigationViewItem.IsExpandedProperty,
            new Binding(nameof(ShellNavigationItem.IsExpanded))
            {
                Mode = BindingMode.TwoWay,
            });

        var activeBinding = new MultiBinding
        {
            Converter = SelectionConverter,
            Mode = BindingMode.OneWay,
        };
        activeBinding.Bindings.Add(new Binding());
        activeBinding.Bindings.Add(CreateShellBinding(shellDataContext, nameof(ShellViewModel.SelectedRouteKey)));
        activeBinding.Bindings.Add(CreateShellBinding(shellDataContext, nameof(ShellViewModel.IsNavigationPaneExpanded)));
        item.SetBinding(NavigationViewItem.IsActiveProperty, activeBinding);

        if (!model.HasChildren)
        {
            item.SetBinding(
                NavigationViewItem.CommandProperty,
                CreateShellBinding(shellDataContext, nameof(ShellViewModel.OpenRouteCommand)));
            item.SetBinding(
                NavigationViewItem.CommandParameterProperty,
                new Binding(nameof(ShellNavigationItem.RouteKey)));
        }

        return item;
    }

    private static Binding CreateShellBinding(object shellDataContext, string path)
    {
        return new Binding(path)
        {
            Source = shellDataContext,
            Mode = BindingMode.OneWay,
        };
    }

    private static SymbolRegular ParseSymbol(string? icon)
    {
        return !string.IsNullOrWhiteSpace(icon)
            && Enum.TryParse(icon, ignoreCase: true, out SymbolRegular symbol)
                ? symbol
                : SymbolRegular.Document24;
    }
}
