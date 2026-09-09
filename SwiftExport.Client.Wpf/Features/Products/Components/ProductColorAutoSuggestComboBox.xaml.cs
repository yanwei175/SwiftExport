using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SwiftExport.Contracts.DTOs.Products;

namespace SwiftExportClient.Features.Products.Components;

/// <summary>基于颜色缓存按中文名称执行本地筛选，并提交完整颜色 DTO。</summary>
public partial class ProductColorAutoSuggestComboBox : UserControl
{
    /// <summary>初始化产品颜色本地缓存选择控件。</summary>
    public ProductColorAutoSuggestComboBox() => InitializeComponent();
    public static readonly DependencyProperty ItemsSourceProperty = Register(nameof(ItemsSource), typeof(IEnumerable));
    public static readonly DependencyProperty SelectedColorProperty = RegisterTwoWay(nameof(SelectedColor), typeof(ProductColorDto));
    public static readonly DependencyProperty SelectionCommittedCommandProperty = Register(nameof(SelectionCommittedCommand), typeof(ICommand));
    public static readonly DependencyProperty SelectionCommittedCommandParameterProperty = Register(nameof(SelectionCommittedCommandParameter), typeof(object));
    public static readonly DependencyProperty SelectionClearedCommandProperty = Register(nameof(SelectionClearedCommand), typeof(ICommand));
    public static readonly DependencyProperty SelectionClearedCommandParameterProperty = Register(nameof(SelectionClearedCommandParameter), typeof(object));
    public static readonly DependencyProperty PlaceholderProperty = Register(nameof(Placeholder), typeof(string), "搜索颜色中文名称");
    /// <summary>获取或设置完整产品颜色缓存。</summary>
    public IEnumerable? ItemsSource { get => (IEnumerable?)GetValue(ItemsSourceProperty); set => SetValue(ItemsSourceProperty, value); }
    /// <summary>获取或设置当前确认的完整颜色 DTO。</summary>
    public ProductColorDto? SelectedColor { get => (ProductColorDto?)GetValue(SelectedColorProperty); set => SetValue(SelectedColorProperty, value); }
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
        DependencyProperty.Register(name, type, typeof(ProductColorAutoSuggestComboBox), new PropertyMetadata(defaultValue));
    private static DependencyProperty RegisterTwoWay(string name, Type type) =>
        DependencyProperty.Register(name, type, typeof(ProductColorAutoSuggestComboBox),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));
}
