using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace SwiftExport.Wpf.EnterpriseGrid.Operations.Internal;

/// <summary>仅负责生成行移动命令按钮的内部原生列。</summary>
internal sealed class LineReorderDataGridColumn(DataGrid owner) : DataGridColumn
{
    private static readonly IMultiValueConverter ButtonEnabledConverter = new ReorderButtonEnabledConverter();

    protected override FrameworkElement GenerateElement(DataGridCell cell, object dataItem) =>
        CreateButtonPanel(dataItem);

    protected override FrameworkElement GenerateEditingElement(DataGridCell cell, object dataItem) =>
        CreateButtonPanel(dataItem);

    private FrameworkElement CreateButtonPanel(object dataItem)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        panel.Children.Add(CreateButton("↑", "上移一行", DataGridLineReorder.MoveUpCommandProperty, dataItem));
        panel.Children.Add(CreateButton("↓", "下移一行", DataGridLineReorder.MoveDownCommandProperty, dataItem));
        return panel;
    }

    private Button CreateButton(
        string content,
        string toolTip,
        DependencyProperty commandProperty,
        object dataItem)
    {
        var button = new Button
        {
            Width = 24d,
            Height = 24d,
            MinWidth = 0d,
            MinHeight = 0d,
            Margin = new Thickness(1d, 0d, 1d, 0d),
            Padding = new Thickness(0d),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Content = content,
            ToolTip = toolTip,
            CommandParameter = dataItem,
            Focusable = false,
            IsTabStop = false,
        };

        BindingOperations.SetBinding(
            button,
            Button.CommandProperty,
            new Binding { Source = owner, Path = new PropertyPath(commandProperty) });

        var enabledBinding = new MultiBinding { Converter = ButtonEnabledConverter };
        enabledBinding.Bindings.Add(new Binding
        {
            Source = owner,
            Path = new PropertyPath(DataGridLineReorder.CanReorderProperty),
        });
        enabledBinding.Bindings.Add(new Binding
        {
            Source = owner,
            Path = new PropertyPath(commandProperty),
        });
        BindingOperations.SetBinding(button, UIElement.IsEnabledProperty, enabledBinding);
        return button;
    }

    private sealed class ReorderButtonEnabledConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
            values.Length >= 2 && values[0] is true && values[1] is ICommand;

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
