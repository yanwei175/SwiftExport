using System.Globalization;
using System.Windows;
using System.Windows.Data;
using Wpf.Ui.Controls;

namespace SwiftExportClient.Shell;

/// <summary>
/// 在 View 层把导航数据转换为 WPF-UI 所需的真实导航控件，避免 ViewModel 依赖控件类型。
/// </summary>
[ValueConversion(typeof(IEnumerable<ShellNavigationItem>), typeof(IReadOnlyList<NavigationViewItem>))]
public sealed class ShellNavigationItemsConverter : IValueConverter
{
    private static readonly RouteSelectionConverter SelectionConverter = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is IEnumerable<ShellNavigationItem> items
            ? CreateItems(items)
            : Array.Empty<NavigationViewItem>();
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }

    private static IReadOnlyList<NavigationViewItem> CreateItems(IEnumerable<ShellNavigationItem> items)
    {
        return items.Select(CreateItem).ToArray();
    }

    private static NavigationViewItem CreateItem(ShellNavigationItem model)
    {
        var item = new NavigationViewItem
        {
            DataContext = model,
            Content = model.Title,
            Icon = new SymbolIcon(ParseSymbol(model.Icon)),
            MenuItemsSource = CreateItems(model.Children),
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
        activeBinding.Bindings.Add(CreateShellBinding(nameof(ShellViewModel.SelectedRouteKey)));
        activeBinding.Bindings.Add(CreateShellBinding(nameof(ShellViewModel.IsNavigationPaneExpanded)));
        item.SetBinding(NavigationViewItem.IsActiveProperty, activeBinding);

        if (!model.HasChildren)
        {
            item.SetBinding(
                NavigationViewItem.CommandProperty,
                CreateShellBinding(nameof(ShellViewModel.OpenRouteCommand)));
            item.SetBinding(
                NavigationViewItem.CommandParameterProperty,
                new Binding(nameof(ShellNavigationItem.RouteKey)));
        }

        return item;
    }

    private static Binding CreateShellBinding(string path)
    {
        return new Binding($"DataContext.{path}")
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(Window), 1),
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
