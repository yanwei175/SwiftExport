using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SwiftExport.Contracts.DTOs.Inventory.WareHouseSite.Picker;

namespace SwiftExportClient.Features.Inventory.WarehouseSites.Components;

/// <summary>基于活动仓库缓存执行本地筛选，并提交完整仓库选择 DTO。</summary>
public partial class WarehouseAutoSuggestComboBox : UserControl
{
    /// <summary>初始化仓库本地缓存选择控件。</summary>
    public WarehouseAutoSuggestComboBox() => InitializeComponent();

    /// <summary>标识可供本地筛选的仓库候选集合。</summary>
    public static readonly DependencyProperty ItemsSourceProperty = Register(nameof(ItemsSource), typeof(IEnumerable));
    /// <summary>当前仓库编码的显示文本；选择提交仍由完整仓库对象决定。</summary>
    public static readonly DependencyProperty TextProperty = Register(nameof(Text), typeof(string), string.Empty);
    /// <summary>标识双向绑定的已选仓库。</summary>
    public static readonly DependencyProperty SelectedWarehouseProperty = RegisterTwoWay(nameof(SelectedWarehouse), typeof(WarehouseSitePickerDto));
    /// <summary>标识选择确认命令。</summary>
    public static readonly DependencyProperty SelectionCommittedCommandProperty = Register(nameof(SelectionCommittedCommand), typeof(ICommand));
    /// <summary>标识选择确认命令参数。</summary>
    public static readonly DependencyProperty SelectionCommittedCommandParameterProperty = Register(nameof(SelectionCommittedCommandParameter), typeof(object));
    /// <summary>标识显式清空命令。</summary>
    public static readonly DependencyProperty SelectionClearedCommandProperty = Register(nameof(SelectionClearedCommand), typeof(ICommand));
    /// <summary>标识显式清空命令参数。</summary>
    public static readonly DependencyProperty SelectionClearedCommandParameterProperty = Register(nameof(SelectionClearedCommandParameter), typeof(object));
    /// <summary>标识空选择时的提示文字。</summary>
    public static readonly DependencyProperty PlaceholderProperty = Register(nameof(Placeholder), typeof(string), "搜索仓库代码或名称");

    /// <summary>获取或设置当前登录会话可见的活动仓库缓存。</summary>
    public IEnumerable? ItemsSource { get => (IEnumerable?)GetValue(ItemsSourceProperty); set => SetValue(ItemsSourceProperty, value); }
    /// <summary>获取或设置输入框显示的仓库编码。</summary>
    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    /// <summary>获取或设置当前确认的完整仓库选择 DTO。</summary>
    public WarehouseSitePickerDto? SelectedWarehouse { get => (WarehouseSitePickerDto?)GetValue(SelectedWarehouseProperty); set => SetValue(SelectedWarehouseProperty, value); }
    /// <summary>获取或设置选择确认命令；未指定参数时传递完整仓库 DTO。</summary>
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
        DependencyProperty.Register(name, type, typeof(WarehouseAutoSuggestComboBox), new PropertyMetadata(defaultValue));

    private static DependencyProperty RegisterTwoWay(string name, Type type) =>
        DependencyProperty.Register(name, type, typeof(WarehouseAutoSuggestComboBox),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));
}
