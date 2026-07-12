using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;

namespace SwiftExport.Wpf.EnterpriseGrid.Columns;

/// <summary>
/// 基于原生 <see cref="DataGridTemplateColumn"/> 的直接点击复选列。
/// Item 与 Header 的 Binding 均由宿主显式提供，框架不约定业务属性名。
/// </summary>
public class CheckBoxDataGridColumn : DataGridTemplateColumn
{
    private static readonly ReadOnlyStateToEnabledConverter EnabledConverter = new();
    private BindingBase? _itemCheckBinding;
    private BindingBase? _allCheckBinding;

    /// <summary>表头 CheckBox 左侧 Margin。</summary>
    public static readonly DependencyProperty HeaderCheckBoxMarginLeftProperty = DependencyProperty.Register(
        nameof(HeaderCheckBoxMarginLeft),
        typeof(double),
        typeof(CheckBoxDataGridColumn),
        new PropertyMetadata(0d, OnHeaderMarginChanged),
        IsValidMargin);

    /// <summary>表头 CheckBox 右侧 Margin。</summary>
    public static readonly DependencyProperty HeaderCheckBoxMarginRightProperty = DependencyProperty.Register(
        nameof(HeaderCheckBoxMarginRight),
        typeof(double),
        typeof(CheckBoxDataGridColumn),
        new PropertyMetadata(0d, OnHeaderMarginChanged),
        IsValidMargin);

    /// <summary>初始化复选列；该列默认不可排序。</summary>
    public CheckBoxDataGridColumn()
    {
        CanUserSort = false;
        GridColumn.SetCanFilter(this, false);
        GridColumn.SetCanExport(this, false);
        Header = string.Empty;
        RefreshTemplates();
    }

    /// <summary>行 CheckBox 的双向 Binding。</summary>
    public BindingBase? ItemCheckBinding
    {
        get => _itemCheckBinding;
        set
        {
            _itemCheckBinding = value;
            RefreshCellTemplates();
        }
    }

    /// <summary>表头全选 CheckBox 的 Binding；DataGridColumn 不继承 DataContext，应使用显式 RelativeSource。</summary>
    public BindingBase? AllCheckBinding
    {
        get => _allCheckBinding;
        set
        {
            _allCheckBinding = value;
            HeaderTemplate = CreateHeaderTemplate();
        }
    }

    /// <summary>获取或设置表头 CheckBox 左侧 Margin。</summary>
    public double HeaderCheckBoxMarginLeft
    {
        get => (double)GetValue(HeaderCheckBoxMarginLeftProperty);
        set => SetValue(HeaderCheckBoxMarginLeftProperty, value);
    }

    /// <summary>获取或设置表头 CheckBox 右侧 Margin。</summary>
    public double HeaderCheckBoxMarginRight
    {
        get => (double)GetValue(HeaderCheckBoxMarginRightProperty);
        set => SetValue(HeaderCheckBoxMarginRightProperty, value);
    }

    private void RefreshTemplates()
    {
        HeaderTemplate = CreateHeaderTemplate();
        RefreshCellTemplates();
    }

    private void RefreshCellTemplates()
    {
        var template = CreateCellTemplate();
        CellTemplate = template;
        CellEditingTemplate = template;
    }

    private DataTemplate CreateHeaderTemplate()
    {
        var checkBoxFactory = CreateCheckBoxFactory();
        checkBoxFactory.SetValue(
            FrameworkElement.MarginProperty,
            new Thickness(HeaderCheckBoxMarginLeft, 0, HeaderCheckBoxMarginRight, 0));
        checkBoxFactory.SetValue(FrameworkElement.ToolTipProperty, "全选/取消全选");
        checkBoxFactory.SetValue(AutomationProperties.NameProperty, "全选/取消全选");
        if (AllCheckBinding is not null)
        {
            checkBoxFactory.SetBinding(
                System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty,
                AllCheckBinding);
        }

        var enabledBinding = new MultiBinding { Converter = EnabledConverter };
        enabledBinding.Bindings.Add(new Binding(nameof(DataGrid.IsReadOnly))
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(DataGrid), 1)
        });
        enabledBinding.Bindings.Add(new Binding(nameof(IsReadOnly)) { Source = this });
        checkBoxFactory.SetBinding(UIElement.IsEnabledProperty, enabledBinding);

        return CreateTemplate(checkBoxFactory);
    }

    private DataTemplate CreateCellTemplate()
    {
        var checkBoxFactory = CreateCheckBoxFactory();
        if (ItemCheckBinding is not null)
        {
            checkBoxFactory.SetBinding(
                System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty,
                ItemCheckBinding);
        }

        var enabledBinding = new MultiBinding { Converter = EnabledConverter };
        enabledBinding.Bindings.Add(new Binding(nameof(DataGrid.IsReadOnly))
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(DataGrid), 1)
        });
        enabledBinding.Bindings.Add(new Binding("Column.IsReadOnly")
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(DataGridCell), 1)
        });
        checkBoxFactory.SetBinding(UIElement.IsEnabledProperty, enabledBinding);

        return CreateTemplate(checkBoxFactory);
    }

    private static FrameworkElementFactory CreateCheckBoxFactory()
    {
        var checkBoxFactory = new FrameworkElementFactory(typeof(CheckBox));
        checkBoxFactory.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        checkBoxFactory.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        checkBoxFactory.SetValue(System.Windows.Controls.Primitives.ToggleButton.IsThreeStateProperty, false);
        return checkBoxFactory;
    }

    private static DataTemplate CreateTemplate(FrameworkElementFactory visualTree)
    {
        var template = new DataTemplate { VisualTree = visualTree };
        template.Seal();
        return template;
    }

    private static void OnHeaderMarginChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e) =>
        ((CheckBoxDataGridColumn)dependencyObject).HeaderTemplate =
            ((CheckBoxDataGridColumn)dependencyObject).CreateHeaderTemplate();

    private static bool IsValidMargin(object value) =>
        value is double margin && double.IsFinite(margin) && margin >= 0;

    private sealed class ReadOnlyStateToEnabledConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
            !values.Any(value => value is true);

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
