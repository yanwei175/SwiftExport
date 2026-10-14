using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SwiftExport.Contracts.DTOs.SystemDtos;

namespace SwiftExportClient.Features.Products.Components;

/// <summary>
/// 在表格和详情表单中选择单个产品属性模板；候选由宿主提供，模板变更确认和业务字段回写由宿主命令负责。
/// </summary>
public partial class ProductAttributeProfileLookupTextBox : UserControl
{
    /// <summary>初始化模板本地搜索选择器，不加载缓存或调用后端。</summary>
    public ProductAttributeProfileLookupTextBox() => InitializeComponent();

    /// <summary>搜索输入和查看文字的依赖属性；业务名称通常由宿主单向提供。</summary>
    public static readonly DependencyProperty TextProperty = RegisterTwoWay(nameof(Text), typeof(string), string.Empty);
    /// <summary>宿主提供的有效模板候选依赖属性。</summary>
    public static readonly DependencyProperty ItemsSourceProperty = Register(nameof(ItemsSource), typeof(IEnumerable));
    /// <summary>选择器当前候选依赖属性；不自动修改业务模板 ID。</summary>
    public static readonly DependencyProperty SelectedProfileProperty = RegisterTwoWay(nameof(SelectedProfile), typeof(SysDictDataDto));
    /// <summary>明确确认候选后执行的宿主命令依赖属性。</summary>
    public static readonly DependencyProperty SelectionCommittedCommandProperty = Register(nameof(SelectionCommittedCommand), typeof(ICommand));
    /// <summary>确认命令参数依赖属性；未指定时沿用内部控件的选中候选参数。</summary>
    public static readonly DependencyProperty SelectionCommittedCommandParameterProperty = Register(nameof(SelectionCommittedCommandParameter), typeof(object));
    /// <summary>用户明确清除时执行的宿主命令依赖属性。</summary>
    public static readonly DependencyProperty SelectionClearedCommandProperty = Register(nameof(SelectionClearedCommand), typeof(ICommand));
    /// <summary>清除命令参数依赖属性。</summary>
    public static readonly DependencyProperty SelectionClearedCommandParameterProperty = Register(nameof(SelectionClearedCommandParameter), typeof(object));
    /// <summary>空模板输入提示依赖属性。</summary>
    public static readonly DependencyProperty PlaceholderProperty = Register(nameof(Placeholder), typeof(string), "搜索属性模板名称或编码");

    /// <summary>获取或设置搜索输入和查看文字，输入本身不提交业务模板变更。</summary>
    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>获取或设置只读模板候选；控件不在字典 DTO 上保存勾选状态。</summary>
    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    /// <summary>获取或设置当前选择器候选；宿主可在模板变更被取消后恢复原候选。</summary>
    public SysDictDataDto? SelectedProfile
    {
        get => (SysDictDataDto?)GetValue(SelectedProfileProperty);
        set => SetValue(SelectedProfileProperty, value);
    }

    /// <summary>获取或设置明确确认候选后的宿主命令。</summary>
    public ICommand? SelectionCommittedCommand
    {
        get => (ICommand?)GetValue(SelectionCommittedCommandProperty);
        set => SetValue(SelectionCommittedCommandProperty, value);
    }

    /// <summary>获取或设置确认命令参数，表格可传递当前行。</summary>
    public object? SelectionCommittedCommandParameter
    {
        get => GetValue(SelectionCommittedCommandParameterProperty);
        set => SetValue(SelectionCommittedCommandParameterProperty, value);
    }

    /// <summary>获取或设置用户明确清除候选后的宿主命令。</summary>
    public ICommand? SelectionClearedCommand
    {
        get => (ICommand?)GetValue(SelectionClearedCommandProperty);
        set => SetValue(SelectionClearedCommandProperty, value);
    }

    /// <summary>获取或设置清除命令参数，表格可传递当前行。</summary>
    public object? SelectionClearedCommandParameter
    {
        get => GetValue(SelectionClearedCommandParameterProperty);
        set => SetValue(SelectionClearedCommandParameterProperty, value);
    }

    /// <summary>获取或设置空模板的搜索提示。</summary>
    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    /// <summary>将焦点交给内部输入框，沿用原生表格编辑器的选择与键盘行为。</summary>
    public void FocusEditor() => LookupEditor.FocusEditor();

    private static DependencyProperty Register(string name, Type type, object? defaultValue = null) =>
        DependencyProperty.Register(name, type, typeof(ProductAttributeProfileLookupTextBox), new PropertyMetadata(defaultValue));

    private static DependencyProperty RegisterTwoWay(string name, Type type, object? defaultValue = null) =>
        DependencyProperty.Register(name, type, typeof(ProductAttributeProfileLookupTextBox),
            new FrameworkPropertyMetadata(defaultValue, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));
}
