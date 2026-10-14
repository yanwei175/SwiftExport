using System.Collections;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SwiftExportClient.Services.Caching;
using SwiftExportClient.Resources.Controls.Filter;

namespace SwiftExportClient.Features.Products.Components;

/// <summary>
/// 复用多选按钮展示产品分类缓存树，以 FakeName 显示中文；各级独立勾选，确定后提交完整分类对象。
/// 可在弹层添加本地未分类选项，沿用确定、取消和清除事务；不加载资料或修改共享缓存。
/// </summary>
public partial class ProductCategoryMutiSelectButton : UserControl
{
    private INotifyCollectionChanged? _itemsSourceNotifier;
    /// <summary>标识按钮标题。</summary>
    public static readonly DependencyProperty HeaderProperty = DependencyProperty.Register(
        nameof(Header), typeof(string), typeof(ProductCategoryMutiSelectButton), new PropertyMetadata("分类"));

    /// <summary>标识前端只读分类树。</summary>
    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource), typeof(IEnumerable<ProductCategoryCacheNode>), typeof(ProductCategoryMutiSelectButton),
        new PropertyMetadata(null, OnPickerSourceChanged));

    /// <summary>标识是否在弹层首项提供未分类；默认关闭，以免改变只允许真实分类的现有调用方。</summary>
    public static readonly DependencyProperty ShowUncategorizedOptionProperty = DependencyProperty.Register(
        nameof(ShowUncategorizedOption), typeof(bool), typeof(ProductCategoryMutiSelectButton),
        new PropertyMetadata(false, OnPickerSourceChanged));

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
    public ProductCategoryMutiSelectButton()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

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

    /// <summary>获取或设置是否提供可与真实分类一起勾选的未分类选项，确定后作为本地特殊节点写回选择集合。</summary>
    public bool ShowUncategorizedOption
    {
        get => (bool)GetValue(ShowUncategorizedOptionProperty);
        set => SetValue(ShowUncategorizedOptionProperty, value);
    }

    /// <summary>重新接线分类来源并一次替换候选快照，让内部控件保留尚未确定的弹层勾选。</summary>
    private static void OnPickerSourceChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var control = (ProductCategoryMutiSelectButton)sender;
        control.AttachItemsSource();
        control.RefreshPickerItems();
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        AttachItemsSource();
        RefreshPickerItems();
    }

    private void OnUnloaded(object sender, RoutedEventArgs args) => DetachItemsSource();

    /// <summary>仅为带特殊项的本地投影订阅缓存变更；普通来源继续由内部多选控件直接监听。</summary>
    private void AttachItemsSource()
    {
        DetachItemsSource();
        if (!IsLoaded || !ShowUncategorizedOption) return;
        _itemsSourceNotifier = ItemsSource as INotifyCollectionChanged;
        if (_itemsSourceNotifier is not null) _itemsSourceNotifier.CollectionChanged += OnItemsSourceChanged;
    }

    private void DetachItemsSource()
    {
        if (_itemsSourceNotifier is not null) _itemsSourceNotifier.CollectionChanged -= OnItemsSourceChanged;
        _itemsSourceNotifier = null;
    }

    private void OnItemsSourceChanged(object? sender, NotifyCollectionChangedEventArgs args) => RefreshPickerItems();

    /// <summary>只把本地未分类节点附加到私有候选快照，不把特殊身份混入共享分类缓存。</summary>
    private void RefreshPickerItems()
    {
        CategoryPicker.SetCurrentValue(MultiSelectLookupButton.ItemsSourceProperty,
            ShowUncategorizedOption
                ? (ItemsSource ?? []).Prepend(ProductCategoryFilterSelection.UncategorizedOption).ToArray()
                : ItemsSource);
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
