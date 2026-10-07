using System.Collections;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using CommunityToolkit.Mvvm.Input;
using SwiftExportClient.Features.QualityControl.Categories.Models;
using SwiftExportClient.Resources.Controls;

namespace SwiftExportClient.Features.QualityControl.Categories.Components;

/// <summary>售后分类树选择器；只做本地搜索和身份提交，父级可选，停用祖先仍可展开查看子级。</summary>
public partial class AfterSalesQualityCategoryLookupTextBox : UserControl, ILookupResultHost
{
    private AfterSalesQualityCategoryLookupNode? _current;
    /// <summary>初始化公共 Lookup 输入行为和树结果宿主。</summary>
    public AfterSalesQualityCategoryLookupTextBox()
    {
        InitializeComponent();
        if (LookupEditor.CustomDropDownContent is FrameworkElement content) content.DataContext = this;
        LookupEditor.SetCustomResultHost(this);
    }

    /// <summary>筛选后的树投影，包括不可选的停用祖先路径。</summary>
    public ObservableCollection<AfterSalesQualityCategoryLookupNode> FilteredRoots { get; } = [];
    /// <summary>公共 Lookup 的精确匹配候选只包含启用节点，避免其默认平面回退选中停用节点。</summary>
    public ObservableCollection<AfterSalesQualityCategoryNode> SelectableItems { get; } = [];
    /// <summary>完整分类根节点；由页面查询并构树，控件不访问 API。</summary>
    public IEnumerable? ItemsSource { get => (IEnumerable?)GetValue(ItemsSourceProperty); set => SetValue(ItemsSourceProperty, value); }
    /// <summary>分类树输入依赖属性。</summary>
    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(nameof(ItemsSource), typeof(IEnumerable), typeof(AfterSalesQualityCategoryLookupTextBox), new PropertyMetadata(null, OnItemsChanged));
    /// <summary>持久化分类身份，只有提交或显式清空才写回。</summary>
    public Guid? SelectedCategoryId { get => (Guid?)GetValue(SelectedCategoryIdProperty); set => SetValue(SelectedCategoryIdProperty, value); }
    /// <summary>支持双向绑定的分类身份。</summary>
    public static readonly DependencyProperty SelectedCategoryIdProperty = DependencyProperty.Register(nameof(SelectedCategoryId), typeof(Guid?), typeof(AfterSalesQualityCategoryLookupTextBox), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnIdentityChanged));
    /// <summary>页面负责显式刷新分类资料。</summary>
    public ICommand? RefreshCommand { get => (ICommand?)GetValue(RefreshCommandProperty); set => SetValue(RefreshCommandProperty, value); }
    /// <summary>刷新命令依赖属性。</summary>
    public static readonly DependencyProperty RefreshCommandProperty = DependencyProperty.Register(nameof(RefreshCommand), typeof(ICommand), typeof(AfterSalesQualityCategoryLookupTextBox));
    /// <summary>显示页面分类请求等待状态。</summary>
    public bool IsRefreshing { get => (bool)GetValue(IsRefreshingProperty); set => SetValue(IsRefreshingProperty, value); }
    /// <summary>请求等待状态依赖属性。</summary>
    public static readonly DependencyProperty IsRefreshingProperty = DependencyProperty.Register(nameof(IsRefreshing), typeof(bool), typeof(AfterSalesQualityCategoryLookupTextBox), new PropertyMetadata(false));

    private IEnumerable<AfterSalesQualityCategoryNode> Roots => ItemsSource?.OfType<AfterSalesQualityCategoryNode>() ?? [];
    bool ILookupResultHost.HasItems => FilteredRoots.Count > 0;
    object? ILookupResultHost.CurrentItem => _current?.Source;

    /// <summary>重建平面精确匹配源后恢复历史显示，加载数据本身不得清空保存的身份。</summary>
    private static void OnItemsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (AfterSalesQualityCategoryLookupTextBox)d;
        control.SelectableItems.Clear();
        foreach (var node in AfterSalesQualityCategoryTree.Flatten(control.Roots).Where(x => x.IsEnabled)) control.SelectableItems.Add(node);
        control.SynchronizeDisplay();
        ((ILookupResultHost)control).Refresh(null, string.Empty);
    }
    private static void OnIdentityChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((AfterSalesQualityCategoryLookupTextBox)d).SynchronizeDisplay();

    /// <summary>历史停用分类仍显示真实名称；未知身份保留引用并显示不可用提示。</summary>
    private void SynchronizeDisplay()
    {
        if (LookupEditor is null) return;
        var node = AfterSalesQualityCategoryTree.Flatten(Roots).FirstOrDefault(x => x.Uid == SelectedCategoryId);
        LookupEditor.SetCurrentValue(LookupTextBox.SelectedItemProperty, node ?? (SelectedCategoryId is { } id
            ? new AfterSalesQualityCategoryNode { Uid = id, CategoryName = "原品质分类（不可用）", IsEnabled = false } : null));
    }

    void ILookupResultHost.Refresh(IEnumerable? itemsSource, string query)
    {
        FilteredRoots.Clear();
        foreach (var root in Roots)
            if (Project(root, query) is { } projection) FilteredRoots.Add(projection);
        Select(Flatten(FilteredRoots).FirstOrDefault(x => x.Source.Uid == SelectedCategoryId)
            ?? Visible(FilteredRoots).FirstOrDefault(x => x.Source.IsEnabled));
    }

    /// <summary>匹配名称、别名和编码，同时保留匹配子节点的祖先。</summary>
    private AfterSalesQualityCategoryLookupNode? Project(AfterSalesQualityCategoryNode source, string query)
    {
        var children = source.Children.Select(x => Project(x, query)).OfType<AfterSalesQualityCategoryLookupNode>().ToArray();
        if (!string.IsNullOrWhiteSpace(query) && children.Length == 0
            && !LookupTextMatcher.IsMatch(source.CategoryName, query) && !LookupTextMatcher.IsMatch(source.FakeName, query)
            && !LookupTextMatcher.IsMatch(source.CategoryCode, query)) return null;
        var node = new AfterSalesQualityCategoryLookupNode(source) { IsExpanded = true, IsSelected = source.Uid == SelectedCategoryId };
        foreach (var child in children) node.Children.Add(child);
        return node;
    }
    void ILookupResultHost.SelectFirst() => Select(Visible(FilteredRoots).FirstOrDefault(x => x.Source.IsEnabled));
    void ILookupResultHost.MoveSelection(int offset)
    {
        var nodes = Visible(FilteredRoots).Where(x => x.Source.IsEnabled).ToArray();
        if (nodes.Length == 0) return;
        Select(nodes[Math.Clamp(Array.IndexOf(nodes, _current) + offset, 0, nodes.Length - 1)]);
    }
    bool ILookupResultHost.TryResolveExact(string text, out object? item)
    {
        item = SelectableItems.FirstOrDefault(x => new[] { x.CategoryName, x.CategoryCode, x.FakeName }
            .Any(value => string.Equals(value.Trim(), text.Trim(), StringComparison.OrdinalIgnoreCase)));
        return item is not null;
    }
    private void Select(AfterSalesQualityCategoryLookupNode? node)
    {
        if (_current is not null) _current.IsSelected = false;
        _current = node;
        if (node is not null) node.IsSelected = true;
    }
    /// <summary>最终提交复核节点资格，停用节点只可显示，不可作为新选择。</summary>
    private void OnCommitted(object? sender, LookupSelectionCommittedEventArgs e)
    {
        if (IsEnabled && e.SelectedItem is AfterSalesQualityCategoryNode { IsEnabled: true } node
            && SelectableItems.Contains(node)) SetCurrentValue(SelectedCategoryIdProperty, node.Uid);
        SynchronizeDisplay();
    }
    /// <summary>显式清空选择，搜索过程中不自动清除原分类。</summary>
    [RelayCommand]
    private void ClearSelection()
    {
        if (IsEnabled) SetCurrentValue(SelectedCategoryIdProperty, null);
        SynchronizeDisplay();
    }
    private void OnTreeSelectionChanged(object sender, RoutedPropertyChangedEventArgs<object> e) => _current = e.NewValue as AfterSalesQualityCategoryLookupNode;
    /// <summary>展开按钮只改变树视图，单击节点正文才提交选择。</summary>
    private void OnMouseCommit(object sender, MouseButtonEventArgs e)
    {
        var source = e.OriginalSource as DependencyObject;
        TreeViewItem? item = null;
        while (source is not null)
        {
            if (source is ToggleButton) return;
            if (source is TreeViewItem found) { item = found; break; }
            source = VisualTreeHelper.GetParent(source);
        }
        if (item?.DataContext is not AfterSalesQualityCategoryLookupNode { Source.IsEnabled: true } node) return;
        Select(node);
        LookupEditor.CommitExternalSelection(node.Source, LookupCommitReason.Mouse);
        e.Handled = true;
    }
    private void OnExpandAll(object sender, RoutedEventArgs e) { foreach (var node in Flatten(FilteredRoots)) node.IsExpanded = true; }
    private void OnCollapseAll(object sender, RoutedEventArgs e) { foreach (var node in Flatten(FilteredRoots)) node.IsExpanded = false; }
    /// <summary>列举全部投影节点，供展开状态操作使用。</summary>
    private static IEnumerable<AfterSalesQualityCategoryLookupNode> Flatten(IEnumerable<AfterSalesQualityCategoryLookupNode> roots)
    {
        foreach (var root in roots) { yield return root; foreach (var child in Flatten(root.Children)) yield return child; }
    }
    /// <summary>键盘导航只遍历可见层级，跳过折叠子树。</summary>
    private static IEnumerable<AfterSalesQualityCategoryLookupNode> Visible(IEnumerable<AfterSalesQualityCategoryLookupNode> roots)
    {
        foreach (var root in roots)
        {
            yield return root;
            if (root.IsExpanded) foreach (var child in Visible(root.Children)) yield return child;
        }
    }
}
