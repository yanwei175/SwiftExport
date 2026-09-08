using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using SwiftExportClient.Services.Caching;

namespace SwiftExportClient.Features.Products.Components;

/// <summary>
/// 显示只读产品分类树，并以父子联动复选框输出稳定的多选筛选快照。
/// 控件只处理展示和选择，不负责调用查询 API。
/// </summary>
public partial class ProductCategoryFilterTreeView : UserControl
{
    /// <summary>标识 <see cref="Categories"/> 依赖属性。</summary>
    public static readonly DependencyProperty CategoriesProperty = DependencyProperty.Register(
        nameof(Categories),
        typeof(IEnumerable<ProductCategoryCacheNode>),
        typeof(ProductCategoryFilterTreeView),
        new PropertyMetadata(null, OnCategoriesChanged));

    /// <summary>标识 <see cref="Selection"/> 依赖属性。</summary>
    public static readonly DependencyProperty SelectionProperty = DependencyProperty.Register(
        nameof(Selection),
        typeof(ProductCategoryFilterSelection),
        typeof(ProductCategoryFilterTreeView),
        new FrameworkPropertyMetadata(
            ProductCategoryFilterSelection.AllCategories,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            OnSelectionChanged,
            CoerceSelection));

    /// <summary>标识 <see cref="SelectionChangedCommand"/> 依赖属性。</summary>
    public static readonly DependencyProperty SelectionChangedCommandProperty = DependencyProperty.Register(
        nameof(SelectionChangedCommand),
        typeof(ICommand),
        typeof(ProductCategoryFilterTreeView),
        new PropertyMetadata(null));

    private readonly ObservableCollection<ProductCategoryFilterNode> _rootNodes = [];
    private INotifyCollectionChanged? _categoriesNotifier;
    private bool _isSynchronizing;

    /// <summary>初始化产品分类筛选树。</summary>
    public ProductCategoryFilterTreeView()
    {
        InitializeComponent();
        RootNodes = new ReadOnlyObservableCollection<ProductCategoryFilterNode>(_rootNodes);
        CategoryTree.ItemsSource = RootNodes;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        RebuildNodes();
    }

    /// <summary>获取或设置现有只读分类树。</summary>
    public IEnumerable<ProductCategoryCacheNode>? Categories
    {
        get => (IEnumerable<ProductCategoryCacheNode>?)GetValue(CategoriesProperty);
        set => SetValue(CategoriesProperty, value);
    }

    /// <summary>获取或设置当前分类筛选快照；该属性默认支持双向绑定。</summary>
    public ProductCategoryFilterSelection Selection
    {
        get => (ProductCategoryFilterSelection)GetValue(SelectionProperty);
        set => SetValue(SelectionProperty, value);
    }

    /// <summary>
    /// 获取或设置用户改变选择后执行的命令；命令参数为最新的 <see cref="Selection"/>。
    /// </summary>
    public ICommand? SelectionChangedCommand
    {
        get => (ICommand?)GetValue(SelectionChangedCommandProperty);
        set => SetValue(SelectionChangedCommandProperty, value);
    }

    /// <summary>获取供内部 TreeView 绑定的只读根节点集合。</summary>
    internal ReadOnlyObservableCollection<ProductCategoryFilterNode> RootNodes { get; }

    private static object CoerceSelection(DependencyObject d, object? baseValue) =>
        baseValue as ProductCategoryFilterSelection ?? ProductCategoryFilterSelection.AllCategories;

    private static void OnCategoriesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (ProductCategoryFilterTreeView)d;
        control.DetachCategoriesNotifier();
        control.AttachCategoriesNotifier();
        control.RebuildNodes();
    }

    private static void OnSelectionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (ProductCategoryFilterTreeView)d;
        if (!control._isSynchronizing)
        {
            control.ApplyExternalSelection((ProductCategoryFilterSelection)e.NewValue);
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        AttachCategoriesNotifier();
        RebuildNodes();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) => DetachCategoriesNotifier();

    private void AttachCategoriesNotifier()
    {
        if (!IsLoaded || _categoriesNotifier is not null || Categories is not INotifyCollectionChanged notifier)
        {
            return;
        }

        _categoriesNotifier = notifier;
        _categoriesNotifier.CollectionChanged += OnCategoriesCollectionChanged;
    }

    private void DetachCategoriesNotifier()
    {
        if (_categoriesNotifier is null)
        {
            return;
        }

        _categoriesNotifier.CollectionChanged -= OnCategoriesCollectionChanged;
        _categoriesNotifier = null;
    }

    private void OnCategoriesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => RebuildNodes();

    /// <summary>
    /// 从只读缓存重新创建 UI 投影，并以分类标识恢复仍有效的选择。
    /// </summary>
    private void RebuildNodes()
    {
        var requestedSelection = Selection;
        _isSynchronizing = true;
        try
        {
            _rootNodes.Clear();
            _rootNodes.Add(ProductCategoryFilterNode.CreateAll());
            _rootNodes.Add(ProductCategoryFilterNode.CreateUncategorized());
            if (Categories is not null)
            {
                foreach (var category in Categories)
                {
                    _rootNodes.Add(ProductCategoryFilterNode.FromCategory(category));
                }
            }

            ApplySelectionCore(requestedSelection);
            SetCurrentValue(SelectionProperty, CreateSelection());
        }
        finally
        {
            _isSynchronizing = false;
        }
    }

    private void ApplyExternalSelection(ProductCategoryFilterSelection selection)
    {
        _isSynchronizing = true;
        try
        {
            ApplySelectionCore(selection);
            // 清除已经不在分类源中的标识，并保持绑定源与当前可见树一致。
            SetCurrentValue(SelectionProperty, CreateSelection());
        }
        finally
        {
            _isSynchronizing = false;
        }
    }

    private void ApplySelectionCore(ProductCategoryFilterSelection selection)
    {
        foreach (var node in Flatten(_rootNodes))
        {
            node.SetSelfSelected(false);
        }

        var allNode = _rootNodes.FirstOrDefault(node => node.Kind == ProductCategoryFilterNodeKind.All);
        if (selection.IsAll)
        {
            allNode?.SetSelfSelected(true);
            RefreshAllCheckStates();
            return;
        }

        var selectedIds = selection.CategoryIds.Where(id => id != Guid.Empty).ToHashSet();
        foreach (var node in Flatten(_rootNodes))
        {
            if (node.Kind == ProductCategoryFilterNodeKind.Uncategorized)
            {
                node.SetSelfSelected(selection.IncludeUncategorized);
            }
            else if (node.Source is not null)
            {
                node.SetSelfSelected(selectedIds.Contains(node.Source.Uid));
            }
        }

        if (!HasActualSelection())
        {
            allNode?.SetSelfSelected(true);
        }

        RefreshAllCheckStates();
    }

    private void CategoryTree_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || e.OriginalSource is not DependencyObject source)
        {
            return;
        }

        // 展开按钮仍使用 TreeView 原生行为；其余节点行区域均可切换筛选状态。
        var toggle = FindVisualAncestor<ToggleButton>(source);
        if (toggle is not null)
        {
            return;
        }

        var item = FindVisualAncestor<TreeViewItem>(source);
        if (item?.DataContext is not ProductCategoryFilterNode node)
        {
            return;
        }

        item.IsSelected = true;
        item.Focus();
        ToggleNode(node);
        e.Handled = true;
    }

    private void CategoryCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { DataContext: ProductCategoryFilterNode node } checkBox)
        {
            var item = FindVisualAncestor<TreeViewItem>(checkBox);
            if (item is not null)
            {
                item.IsSelected = true;
                item.Focus();
            }

            ToggleNode(node);
        }

        e.Handled = true;
    }

    private void CategoryTree_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Space
            || CategoryTree.SelectedItem is not ProductCategoryFilterNode node)
        {
            return;
        }

        ToggleNode(node);
        e.Handled = true;
    }

    /// <summary>
    /// 按一次用户动作原子更新节点、自身筛选快照和宿主命令，避免父子联动重复查询。
    /// </summary>
    private void ToggleNode(ProductCategoryFilterNode node)
    {
        if (node.Kind == ProductCategoryFilterNodeKind.All)
        {
            foreach (var current in Flatten(_rootNodes))
            {
                current.SetSelfSelected(current.Kind == ProductCategoryFilterNodeKind.All);
            }
        }
        else
        {
            var shouldSelect = node.IsChecked != true;
            node.SetSubtreeSelected(shouldSelect);
            _rootNodes.First(current => current.Kind == ProductCategoryFilterNodeKind.All)
                .SetSelfSelected(false);
            if (!HasActualSelection())
            {
                _rootNodes.First(current => current.Kind == ProductCategoryFilterNodeKind.All)
                    .SetSelfSelected(true);
            }
        }

        RefreshAllCheckStates();
        PublishUserSelection();
    }

    private bool HasActualSelection() => Flatten(_rootNodes).Any(node =>
        node.Kind != ProductCategoryFilterNodeKind.All && node.IsSelfSelected);

    private ProductCategoryFilterSelection CreateSelection()
    {
        var allNode = _rootNodes.FirstOrDefault(node => node.Kind == ProductCategoryFilterNodeKind.All);
        if (allNode?.IsSelfSelected != false)
        {
            return ProductCategoryFilterSelection.AllCategories;
        }

        var includeUncategorized = _rootNodes.Any(node =>
            node.Kind == ProductCategoryFilterNodeKind.Uncategorized && node.IsSelfSelected);
        var categoryIds = Flatten(_rootNodes)
            .Where(node => node.Kind == ProductCategoryFilterNodeKind.Category && node.IsSelfSelected)
            .Select(node => node.Source!.Uid)
            .Distinct()
            .ToArray();
        return new ProductCategoryFilterSelection(false, includeUncategorized, categoryIds);
    }

    private void PublishUserSelection()
    {
        var selection = CreateSelection();
        _isSynchronizing = true;
        try
        {
            SetCurrentValue(SelectionProperty, selection);
        }
        finally
        {
            _isSynchronizing = false;
        }

        if (SelectionChangedCommand?.CanExecute(selection) == true)
        {
            SelectionChangedCommand.Execute(selection);
        }
    }

    private void RefreshAllCheckStates()
    {
        foreach (var node in Flatten(_rootNodes).Reverse())
        {
            node.RefreshCheckState();
        }
    }

    private static IEnumerable<ProductCategoryFilterNode> Flatten(
        IEnumerable<ProductCategoryFilterNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in Flatten(node.Children))
            {
                yield return child;
            }
        }
    }

    private static T? FindVisualAncestor<T>(DependencyObject? current)
        where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }
}
