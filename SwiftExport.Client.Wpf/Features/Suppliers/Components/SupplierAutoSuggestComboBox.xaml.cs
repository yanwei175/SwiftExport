using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SwiftExport.Contracts.DTOs.Suppliers;

namespace SwiftExportClient.Features.Suppliers.Components;

/// <summary>基于供应商缓存按供应商编码执行本地筛选，并提交完整供应商 DTO。</summary>
public partial class SupplierAutoSuggestComboBox : UserControl
{
    /// <summary>初始化供应商本地缓存选择控件。</summary>
    public SupplierAutoSuggestComboBox() => InitializeComponent();
    /// <summary>标识内部编辑区域是否填满宿主高度，默认按内容高度显示。</summary>
    public static readonly DependencyProperty StretchToHostProperty = Register(nameof(StretchToHost), typeof(bool), false);

    /// <summary>获取或设置是否填满单元格等宿主的高度。</summary>
    public bool StretchToHost { get => (bool)GetValue(StretchToHostProperty); set => SetValue(StretchToHostProperty, value); }

    public static readonly DependencyProperty ItemsSourceProperty = Register(nameof(ItemsSource), typeof(IEnumerable));
    public static readonly DependencyProperty SelectedSupplierProperty = RegisterTwoWay(nameof(SelectedSupplier), typeof(SupplierDto));
    public static readonly DependencyProperty SelectionCommittedCommandProperty = Register(nameof(SelectionCommittedCommand), typeof(ICommand));
    public static readonly DependencyProperty SelectionCommittedCommandParameterProperty = Register(nameof(SelectionCommittedCommandParameter), typeof(object));
    public static readonly DependencyProperty SelectionClearedCommandProperty = Register(nameof(SelectionClearedCommand), typeof(ICommand));
    public static readonly DependencyProperty SelectionClearedCommandParameterProperty = Register(nameof(SelectionClearedCommandParameter), typeof(object));
    public static readonly DependencyProperty PlaceholderProperty = Register(nameof(Placeholder), typeof(string), "搜索供应商编码");
    /// <summary>获取或设置完整供应商缓存。</summary>
    public IEnumerable? ItemsSource { get => (IEnumerable?)GetValue(ItemsSourceProperty); set => SetValue(ItemsSourceProperty, value); }
    /// <summary>获取或设置当前确认的完整供应商 DTO。</summary>
    public SupplierDto? SelectedSupplier { get => (SupplierDto?)GetValue(SelectedSupplierProperty); set => SetValue(SelectedSupplierProperty, value); }
    /// <summary>获取或设置选择确认命令；未指定参数时传递完整 DTO。</summary>
    public ICommand? SelectionCommittedCommand { get => (ICommand?)GetValue(SelectionCommittedCommandProperty); set => SetValue(SelectionCommittedCommandProperty, value); }
    /// <summary>获取或设置选择确认命令的业务上下文。</summary>
    public object? SelectionCommittedCommandParameter { get => GetValue(SelectionCommittedCommandParameterProperty); set => SetValue(SelectionCommittedCommandParameterProperty, value); }
    /// <summary>获取或设置显式清空选择后的命令。</summary>
    public ICommand? SelectionClearedCommand { get => (ICommand?)GetValue(SelectionClearedCommandProperty); set => SetValue(SelectionClearedCommandProperty, value); }
    /// <summary>获取或设置清空命令的业务上下文。</summary>
    public object? SelectionClearedCommandParameter { get => GetValue(SelectionClearedCommandParameterProperty); set => SetValue(SelectionClearedCommandParameterProperty, value); }
    /// <summary>获取或设置空选择提示文字。</summary>
    public string Placeholder { get => (string)GetValue(PlaceholderProperty); set => SetValue(PlaceholderProperty, value); }
    private static DependencyProperty Register(string name, Type type, object? defaultValue = null) =>
        DependencyProperty.Register(name, type, typeof(SupplierAutoSuggestComboBox), new PropertyMetadata(defaultValue));
    private static DependencyProperty RegisterTwoWay(string name, Type type) =>
        DependencyProperty.Register(name, type, typeof(SupplierAutoSuggestComboBox),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));
}
