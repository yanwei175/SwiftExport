using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SwiftExportClient.Features.Products.Components.Models;

namespace SwiftExportClient.Features.Products.Components;

/// <summary>
/// 主型号远程搜索编辑器。复用产品 Lookup 的交互，搜索由调用方负责；仅封装主型号选择和候选视觉。
/// </summary>
public partial class ProductMasterLookupTextBox : UserControl
{
    /// <summary>初始化主型号候选模板与公共 Lookup 绑定。</summary>
    public ProductMasterLookupTextBox()
    {
        InitializeComponent();
    }

    /// <summary>标识输入及选中型号显示文本依赖属性。</summary>
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text),
        typeof(string),
        typeof(ProductMasterLookupTextBox),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    /// <summary>获取或设置输入及选中型号显示文本。</summary>
    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>标识主型号候选集合依赖属性。</summary>
    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource),
        typeof(IEnumerable),
        typeof(ProductMasterLookupTextBox),
        new PropertyMetadata(null));

    /// <summary>获取或设置主型号候选集合。</summary>
    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    /// <summary>标识当前选中的完整主型号候选依赖属性。</summary>
    public static readonly DependencyProperty SelectedProductMasterProperty = DependencyProperty.Register(
        nameof(SelectedProductMaster),
        typeof(ProductMasterLookupItemModel),
        typeof(ProductMasterLookupTextBox),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    /// <summary>获取或设置当前选中的完整主型号候选。</summary>
    public ProductMasterLookupItemModel? SelectedProductMaster
    {
        get => (ProductMasterLookupItemModel?)GetValue(SelectedProductMasterProperty);
        set => SetValue(SelectedProductMasterProperty, value);
    }

    /// <summary>标识当前选中主型号的 Uid依赖属性。</summary>
    public static readonly DependencyProperty SelectedProductMasterIdProperty = DependencyProperty.Register(
        nameof(SelectedProductMasterId),
        typeof(Guid?),
        typeof(ProductMasterLookupTextBox),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    /// <summary>获取或设置当前选中主型号的 Uid。</summary>
    public Guid? SelectedProductMasterId
    {
        get => (Guid?)GetValue(SelectedProductMasterIdProperty);
        set => SetValue(SelectedProductMasterIdProperty, value);
    }

    /// <summary>标识调用方提供的远程搜索命令依赖属性。</summary>
    public static readonly DependencyProperty SearchCommandProperty = DependencyProperty.Register(
        nameof(SearchCommand),
        typeof(ICommand),
        typeof(ProductMasterLookupTextBox),
        new PropertyMetadata(null));

    /// <summary>获取或设置调用方提供的远程搜索命令。</summary>
    public ICommand? SearchCommand
    {
        get => (ICommand?)GetValue(SearchCommandProperty);
        set => SetValue(SearchCommandProperty, value);
    }

    /// <summary>标识确认选择后通知调用方的命令依赖属性。</summary>
    public static readonly DependencyProperty SelectionCommittedCommandProperty = DependencyProperty.Register(
        nameof(SelectionCommittedCommand),
        typeof(ICommand),
        typeof(ProductMasterLookupTextBox),
        new PropertyMetadata(null));

    /// <summary>获取或设置确认选择后通知调用方的命令。</summary>
    public ICommand? SelectionCommittedCommand
    {
        get => (ICommand?)GetValue(SelectionCommittedCommandProperty);
        set => SetValue(SelectionCommittedCommandProperty, value);
    }

    /// <summary>标识确认选择命令的业务参数依赖属性。</summary>
    public static readonly DependencyProperty SelectionCommittedCommandParameterProperty = DependencyProperty.Register(
        nameof(SelectionCommittedCommandParameter),
        typeof(object),
        typeof(ProductMasterLookupTextBox),
        new PropertyMetadata(null));

    /// <summary>获取或设置确认选择命令的业务参数。</summary>
    public object? SelectionCommittedCommandParameter
    {
        get => GetValue(SelectionCommittedCommandParameterProperty);
        set => SetValue(SelectionCommittedCommandParameterProperty, value);
    }

    /// <summary>标识显式清空选择时通知调用方的命令依赖属性。</summary>
    public static readonly DependencyProperty SelectionClearedCommandProperty = DependencyProperty.Register(
        nameof(SelectionClearedCommand), typeof(ICommand), typeof(ProductMasterLookupTextBox), new PropertyMetadata(null));

    /// <summary>获取或设置显式清空选择时通知调用方的命令。</summary>
    public ICommand? SelectionClearedCommand
    {
        get => (ICommand?)GetValue(SelectionClearedCommandProperty);
        set => SetValue(SelectionClearedCommandProperty, value);
    }

    /// <summary>标识清空选择命令的业务参数依赖属性。</summary>
    public static readonly DependencyProperty SelectionClearedCommandParameterProperty = DependencyProperty.Register(
        nameof(SelectionClearedCommandParameter), typeof(object), typeof(ProductMasterLookupTextBox), new PropertyMetadata(null));

    /// <summary>获取或设置清空选择命令的业务参数。</summary>
    public object? SelectionClearedCommandParameter
    {
        get => GetValue(SelectionClearedCommandParameterProperty);
        set => SetValue(SelectionClearedCommandParameterProperty, value);
    }

    /// <summary>标识调用方的远程搜索等待状态依赖属性。</summary>
    public static readonly DependencyProperty IsBusyProperty = DependencyProperty.Register(
        nameof(IsBusy),
        typeof(bool),
        typeof(ProductMasterLookupTextBox),
        new PropertyMetadata(false));

    /// <summary>获取或设置调用方的远程搜索等待状态。</summary>
    public bool IsBusy
    {
        get => (bool)GetValue(IsBusyProperty);
        set => SetValue(IsBusyProperty, value);
    }

    /// <summary>标识用户输入后的防抖间隔依赖属性。</summary>
    public static readonly DependencyProperty SearchDelayProperty = DependencyProperty.Register(
        nameof(SearchDelay),
        typeof(TimeSpan),
        typeof(ProductMasterLookupTextBox),
        new PropertyMetadata(TimeSpan.FromMilliseconds(300)),
        value => value is TimeSpan delay && delay >= TimeSpan.Zero);

    /// <summary>获取或设置用户输入后的防抖间隔。</summary>
    public TimeSpan SearchDelay
    {
        get => (TimeSpan)GetValue(SearchDelayProperty);
        set => SetValue(SearchDelayProperty, value);
    }

    /// <summary>标识触发文本搜索的最小字符数依赖属性。</summary>
    public static readonly DependencyProperty MinimumSearchLengthProperty = DependencyProperty.Register(
        nameof(MinimumSearchLength),
        typeof(int),
        typeof(ProductMasterLookupTextBox),
        new PropertyMetadata(1, null, static (_, value) => Math.Max(0, (int)value)));

    /// <summary>获取或设置触发文本搜索的最小字符数。</summary>
    public int MinimumSearchLength
    {
        get => (int)GetValue(MinimumSearchLengthProperty);
        set => SetValue(MinimumSearchLengthProperty, value);
    }

    /// <summary>标识未输入时的提示文本依赖属性。</summary>
    public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.Register(
        nameof(Placeholder),
        typeof(string),
        typeof(ProductMasterLookupTextBox),
        new PropertyMetadata("搜索主型号或描述"));

    /// <summary>获取或设置未输入时的提示文本。</summary>
    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    /// <summary>将键盘焦点交给公共 Lookup 编辑器，不主动触发查询。</summary>
    public void FocusEditor() => LookupEditor.FocusEditor();
}
