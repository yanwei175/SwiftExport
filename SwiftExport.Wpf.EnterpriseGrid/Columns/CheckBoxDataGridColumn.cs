using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

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

    /// <summary>表头 CheckBox 水平视觉偏移；正数向右，负数向左，默认 0。</summary>
    public static readonly DependencyProperty HeaderCheckBoxHorizontalOffsetProperty = DependencyProperty.Register(
        nameof(HeaderCheckBoxHorizontalOffset),
        typeof(double),
        typeof(CheckBoxDataGridColumn),
        new PropertyMetadata(0d, OnHeaderAppearanceChanged),
        IsValidOffset);

    /// <summary>表头 CheckBox 宽度；默认 Auto。</summary>
    public static readonly DependencyProperty HeaderCheckBoxWidthProperty = DependencyProperty.Register(
        nameof(HeaderCheckBoxWidth),
        typeof(double),
        typeof(CheckBoxDataGridColumn),
        new PropertyMetadata(double.NaN, OnHeaderAppearanceChanged),
        IsValidDimension);

    /// <summary>表头 CheckBox 高度；默认 Auto。</summary>
    public static readonly DependencyProperty HeaderCheckBoxHeightProperty = DependencyProperty.Register(
        nameof(HeaderCheckBoxHeight),
        typeof(double),
        typeof(CheckBoxDataGridColumn),
        new PropertyMetadata(double.NaN, OnHeaderAppearanceChanged),
        IsValidDimension);

    /// <summary>单元格 CheckBox 宽度；默认 Auto。</summary>
    public static readonly DependencyProperty CellCheckBoxWidthProperty = DependencyProperty.Register(
        nameof(CellCheckBoxWidth),
        typeof(double),
        typeof(CheckBoxDataGridColumn),
        new PropertyMetadata(double.NaN, OnCellAppearanceChanged),
        IsValidDimension);

    /// <summary>单元格 CheckBox 高度；默认 Auto。</summary>
    public static readonly DependencyProperty CellCheckBoxHeightProperty = DependencyProperty.Register(
        nameof(CellCheckBoxHeight),
        typeof(double),
        typeof(CheckBoxDataGridColumn),
        new PropertyMetadata(double.NaN, OnCellAppearanceChanged),
        IsValidDimension);

    /// <summary>表头 CheckBox 样式；为空时使用宿主主题默认样式。</summary>
    public static readonly DependencyProperty HeaderCheckBoxStyleProperty = DependencyProperty.Register(
        nameof(HeaderCheckBoxStyle),
        typeof(Style),
        typeof(CheckBoxDataGridColumn),
        new PropertyMetadata(null, OnHeaderAppearanceChanged));

    /// <summary>单元格 CheckBox 样式；为空时使用宿主主题默认样式。</summary>
    public static readonly DependencyProperty CellCheckBoxStyleProperty = DependencyProperty.Register(
        nameof(CellCheckBoxStyle),
        typeof(Style),
        typeof(CheckBoxDataGridColumn),
        new PropertyMetadata(null, OnCellAppearanceChanged));

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

    /// <summary>获取或设置表头 CheckBox 水平视觉偏移；正数向右，负数向左。</summary>
    public double HeaderCheckBoxHorizontalOffset
    {
        get => (double)GetValue(HeaderCheckBoxHorizontalOffsetProperty);
        set => SetValue(HeaderCheckBoxHorizontalOffsetProperty, value);
    }

    /// <summary>获取或设置表头 CheckBox 宽度；使用 NaN 表示 Auto。</summary>
    public double HeaderCheckBoxWidth
    {
        get => (double)GetValue(HeaderCheckBoxWidthProperty);
        set => SetValue(HeaderCheckBoxWidthProperty, value);
    }

    /// <summary>获取或设置表头 CheckBox 高度；使用 NaN 表示 Auto。</summary>
    public double HeaderCheckBoxHeight
    {
        get => (double)GetValue(HeaderCheckBoxHeightProperty);
        set => SetValue(HeaderCheckBoxHeightProperty, value);
    }

    /// <summary>获取或设置单元格 CheckBox 宽度；使用 NaN 表示 Auto。</summary>
    public double CellCheckBoxWidth
    {
        get => (double)GetValue(CellCheckBoxWidthProperty);
        set => SetValue(CellCheckBoxWidthProperty, value);
    }

    /// <summary>获取或设置单元格 CheckBox 高度；使用 NaN 表示 Auto。</summary>
    public double CellCheckBoxHeight
    {
        get => (double)GetValue(CellCheckBoxHeightProperty);
        set => SetValue(CellCheckBoxHeightProperty, value);
    }

    /// <summary>获取或设置表头 CheckBox 样式。</summary>
    public Style? HeaderCheckBoxStyle
    {
        get => (Style?)GetValue(HeaderCheckBoxStyleProperty);
        set => SetValue(HeaderCheckBoxStyleProperty, value);
    }

    /// <summary>获取或设置单元格 CheckBox 样式。</summary>
    public Style? CellCheckBoxStyle
    {
        get => (Style?)GetValue(CellCheckBoxStyleProperty);
        set => SetValue(CellCheckBoxStyleProperty, value);
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
        var checkBoxFactory = CreateCheckBoxFactory(HeaderCheckBoxStyle);
        var viewboxFactory = CreateViewboxFactory(HeaderCheckBoxWidth, HeaderCheckBoxHeight);
        var gridFactory = new FrameworkElementFactory(typeof(Grid));
        gridFactory.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
        gridFactory.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Stretch);
        viewboxFactory.SetValue(
            UIElement.RenderTransformProperty,
            new TranslateTransform(HeaderCheckBoxHorizontalOffset, 0));
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
        viewboxFactory.AppendChild(checkBoxFactory);
        gridFactory.AppendChild(viewboxFactory);

        return CreateTemplate(gridFactory);
    }

    private DataTemplate CreateCellTemplate()
    {
        var checkBoxFactory = CreateCheckBoxFactory(CellCheckBoxStyle);
        var viewboxFactory = CreateViewboxFactory(CellCheckBoxWidth, CellCheckBoxHeight);
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
        viewboxFactory.AppendChild(checkBoxFactory);

        return CreateTemplate(viewboxFactory);
    }

    private static FrameworkElementFactory CreateCheckBoxFactory(Style? style)
    {
        var checkBoxFactory = new FrameworkElementFactory(typeof(CheckBox));
        checkBoxFactory.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        checkBoxFactory.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        checkBoxFactory.SetValue(System.Windows.Controls.Primitives.ToggleButton.IsThreeStateProperty, false);
        if (style is not null)
        {
            checkBoxFactory.SetValue(FrameworkElement.StyleProperty, style);
        }

        return checkBoxFactory;
    }

    private static FrameworkElementFactory CreateViewboxFactory(double width, double height)
    {
        var viewboxFactory = new FrameworkElementFactory(typeof(Viewbox));
        viewboxFactory.SetValue(FrameworkElement.WidthProperty, width);
        viewboxFactory.SetValue(FrameworkElement.HeightProperty, height);
        viewboxFactory.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        viewboxFactory.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        viewboxFactory.SetValue(Viewbox.StretchProperty, System.Windows.Media.Stretch.Uniform);
        viewboxFactory.SetValue(Viewbox.StretchDirectionProperty, StretchDirection.Both);
        return viewboxFactory;
    }

    private static DataTemplate CreateTemplate(FrameworkElementFactory visualTree)
    {
        var template = new DataTemplate { VisualTree = visualTree };
        template.Seal();
        return template;
    }

    private static void OnHeaderAppearanceChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e) =>
        ((CheckBoxDataGridColumn)dependencyObject).HeaderTemplate =
            ((CheckBoxDataGridColumn)dependencyObject).CreateHeaderTemplate();

    private static void OnCellAppearanceChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e) =>
        ((CheckBoxDataGridColumn)dependencyObject).RefreshCellTemplates();

    private static bool IsValidOffset(object value) =>
        value is double offset && double.IsFinite(offset);

    private static bool IsValidDimension(object value) =>
        value is double dimension && (double.IsNaN(dimension) || double.IsFinite(dimension) && dimension >= 0);

    private sealed class ReadOnlyStateToEnabledConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
            !values.Any(value => value is true);

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
