using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SwiftExportClient.Services.Caching;

namespace SwiftExportClient.Features.Products.Components;

/// <summary>
/// 复用多选按钮展示产品分类缓存树，以 FakeName 显示中文；各级独立勾选，确定后提交完整分类对象。
/// 数据由页面绑定缓存 ProductCategoryTree，本控件不加载资料或修改缓存。
/// </summary>
public partial class ProductCategoryMutiSelectButton : UserControl
{
    /// <summary>标识按钮标题。</summary>
    public static readonly DependencyProperty HeaderProperty = DependencyProperty.Register(
        nameof(Header), typeof(string), typeof(ProductCategoryMutiSelectButton), new PropertyMetadata("分类"));

    /// <summary>标识前端只读分类树。</summary>
    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource), typeof(IEnumerable<ProductCategoryCacheNode>), typeof(ProductCategoryMutiSelectButton));

    /// <summary>标识确定后写回的完整分类对象集合。</summary>
    public static readonly DependencyProperty SelectedItemsProperty = DependencyProperty.Register(
        nameof(SelectedItems), typeof(IList), typeof(ProductCategoryMutiSelectButton),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    /// <summary>标识选择确定后执行的命令。</summary>
    public static readonly DependencyProperty SelectionCommittedCommandProperty = DependencyProperty.Register(
        nameof(SelectionCommittedCommand), typeof(ICommand), typeof(ProductCategoryMutiSelectButton));

    /// <summary>标识选择确定命令的参数。</summary>
    public static readonly DependencyProperty SelectionCommittedCommandParameterProperty = DependencyProperty.Register(
        nameof(SelectionCommittedCommandParameter), typeof(object), typeof(ProductCategoryMutiSelectButton));

    /// <summary>初始化以中文名称展示的分类树多选按钮。</summary>
    public ProductCategoryMutiSelectButton() => InitializeComponent();

    /// <summary>获取或设置按钮标题，默认“分类”；已选数量由内部按钮追加。</summary>
    public string Header
    {
        get => (string)GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    /// <summary>获取或设置缓存 ProductCategoryTree；选中状态仅保存在弹层投影。</summary>
    public IEnumerable<ProductCategoryCacheNode>? ItemsSource
    {
        get => (IEnumerable<ProductCategoryCacheNode>?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    /// <summary>获取或设置可修改的已选分类集合；关闭取消，清除后仍需确定才回写。</summary>
    public IList? SelectedItems
    {
        get => (IList?)GetValue(SelectedItemsProperty);
        set => SetValue(SelectedItemsProperty, value);
    }

    /// <summary>获取或设置确定后执行的命令；缓存刷新和外部集合变化不会触发。</summary>
    public ICommand? SelectionCommittedCommand
    {
        get => (ICommand?)GetValue(SelectionCommittedCommandProperty);
        set => SetValue(SelectionCommittedCommandProperty, value);
    }

    /// <summary>获取或设置选择确定命令的参数。</summary>
    public object? SelectionCommittedCommandParameter
    {
        get => GetValue(SelectionCommittedCommandParameterProperty);
        set => SetValue(SelectionCommittedCommandParameterProperty, value);
    }
}
