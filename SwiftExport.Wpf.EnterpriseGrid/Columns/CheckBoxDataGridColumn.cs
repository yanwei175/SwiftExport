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
    private readonly Border _headerContainer;
    private readonly CheckBox _headerCheckBox;
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

        _headerCheckBox = new CheckBox
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsThreeState = false,
            ToolTip = "全选/取消全选"
        };
        AutomationProperties.SetName(_headerCheckBox, "全选/取消全选");
        _headerContainer = new Border
        {
            Background = System.Windows.Media.Brushes.Transparent,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Child = _headerCheckBox
        };
        Header = _headerContainer;
        UpdateHeaderMargin();
        ApplyHeaderEnabledBinding();
    }

    /// <summary>行 CheckBox 的双向 Binding。</summary>
    public BindingBase? ItemCheckBinding { get; set; }

    /// <summary>表头全选 CheckBox 的 Binding；DataGridColumn 不继承 DataContext，应使用显式 RelativeSource。</summary>
    public BindingBase? AllCheckBinding
    {
        get => _allCheckBinding;
        set
        {
            _allCheckBinding = value;
            BindingOperations.ClearBinding(_headerCheckBox, System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty);
            if (value is not null)
            {
                BindingOperations.SetBinding(
                    _headerCheckBox,
                    System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty,
                    value);
            }
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

    /// <inheritdoc />
    protected override FrameworkElement GenerateElement(DataGridCell cell, object dataItem) =>
        CreateCellCheckBox();

    /// <inheritdoc />
    protected override FrameworkElement GenerateEditingElement(DataGridCell cell, object dataItem) =>
        CreateCellCheckBox();

    private CheckBox CreateCellCheckBox()
    {
        var checkBox = new CheckBox
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsThreeState = false
        };
        if (ItemCheckBinding is not null)
        {
            BindingOperations.SetBinding(
                checkBox,
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
        BindingOperations.SetBinding(checkBox, UIElement.IsEnabledProperty, enabledBinding);
        return checkBox;
    }

    private void ApplyHeaderEnabledBinding()
    {
        var enabledBinding = new MultiBinding { Converter = EnabledConverter };
        enabledBinding.Bindings.Add(new Binding(nameof(DataGrid.IsReadOnly))
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(DataGrid), 1)
        });
        enabledBinding.Bindings.Add(new Binding(nameof(IsReadOnly)) { Source = this });
        BindingOperations.SetBinding(_headerCheckBox, UIElement.IsEnabledProperty, enabledBinding);
    }

    private static void OnHeaderMarginChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e) =>
        ((CheckBoxDataGridColumn)dependencyObject).UpdateHeaderMargin();

    private void UpdateHeaderMargin()
    {
        // Margin 直接作用于窄列中的 CheckBox 会扩大布局尺寸并被 Header 裁剪。
        // 公开 API 保持 Margin 命名，但内部映射为容器 Padding，只调整对齐而不越出列边界。
        _headerContainer.Padding = new Thickness(HeaderCheckBoxMarginLeft, 0, HeaderCheckBoxMarginRight, 0);
    }

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
