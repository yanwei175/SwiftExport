using System.Collections;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using SwiftExportClient.Resources.Controls;
using SwiftExportClient.Services.Caching;

namespace SwiftExportClient.Features.Products.Components;

/// <summary>
/// 基于产品分类只读缓存的树形 Lookup。控件只负责本地筛选和选择，不直接访问缓存服务或 API。
/// </summary>
public partial class ProductCategoryLookupTextBox : UserControl, ILookupResultHost
{
    private readonly ObservableCollection<ProductCategoryLookupNode> _filteredRoots = [];
    private ProductCategoryLookupNode? _currentNode;
    private bool _isSynchronizingSelection;

    public ProductCategoryLookupTextBox()
    {
        FilteredRoots = new ReadOnlyObservableCollection<ProductCategoryLookupNode>(_filteredRoots);
        InitializeComponent();
        if (LookupEditor.CustomDropDownContent is FrameworkElement dropDownContent)
        {
            dropDownContent.DataContext = this;
        }
        LookupEditor.SetCustomResultHost(this);
    }

    public ReadOnlyObservableCollection<ProductCategoryLookupNode> FilteredRoots { get; }

    public static readonly DependencyProperty TreeExpansionActionTextProperty = DependencyProperty.Register(
        nameof(TreeExpansionActionText),
        typeof(string),
        typeof(ProductCategoryLookupTextBox),
        new PropertyMetadata("展开全部"));

    public string TreeExpansionActionText
    {
        get => (string)GetValue(TreeExpansionActionTextProperty);
        private set => SetValue(TreeExpansionActionTextProperty, value);
    }

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(ProductCategoryLookupTextBox),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource), typeof(IEnumerable), typeof(ProductCategoryLookupTextBox),
        new PropertyMetadata(null));

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public static readonly DependencyProperty SelectedCategoryProperty = DependencyProperty.Register(
        nameof(SelectedCategory), typeof(ProductCategoryCacheNode), typeof(ProductCategoryLookupTextBox),
        new FrameworkPropertyMetadata(
            null,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            OnSelectedCategoryChanged));

    public ProductCategoryCacheNode? SelectedCategory
    {
        get => (ProductCategoryCacheNode?)GetValue(SelectedCategoryProperty);
        set => SetValue(SelectedCategoryProperty, value);
    }

    public static readonly DependencyProperty SelectedCategoryIdProperty = DependencyProperty.Register(
        nameof(SelectedCategoryId), typeof(Guid?), typeof(ProductCategoryLookupTextBox),
        new FrameworkPropertyMetadata(
            null,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            OnSelectedCategoryIdChanged));

    public Guid? SelectedCategoryId
    {
        get => (Guid?)GetValue(SelectedCategoryIdProperty);
        set => SetValue(SelectedCategoryIdProperty, value);
    }

    public static readonly DependencyProperty RefreshCommandProperty = RegisterCommand(nameof(RefreshCommand));
    public static readonly DependencyProperty SelectionCommittedCommandProperty = RegisterCommand(nameof(SelectionCommittedCommand));
    public static readonly DependencyProperty SelectionClearedCommandProperty = RegisterCommand(nameof(SelectionClearedCommand));

    public ICommand? RefreshCommand
    {
        get => (ICommand?)GetValue(RefreshCommandProperty);
        set => SetValue(RefreshCommandProperty, value);
    }

    public ICommand? SelectionCommittedCommand
    {
        get => (ICommand?)GetValue(SelectionCommittedCommandProperty);
        set => SetValue(SelectionCommittedCommandProperty, value);
    }

    public ICommand? SelectionClearedCommand
    {
        get => (ICommand?)GetValue(SelectionClearedCommandProperty);
        set => SetValue(SelectionClearedCommandProperty, value);
    }

    public static readonly DependencyProperty SelectionCommittedCommandParameterProperty =
        RegisterObject(nameof(SelectionCommittedCommandParameter));
    public static readonly DependencyProperty SelectionClearedCommandParameterProperty =
        RegisterObject(nameof(SelectionClearedCommandParameter));

    public object? SelectionCommittedCommandParameter
    {
        get => GetValue(SelectionCommittedCommandParameterProperty);
        set => SetValue(SelectionCommittedCommandParameterProperty, value);
    }

    public object? SelectionClearedCommandParameter
    {
        get => GetValue(SelectionClearedCommandParameterProperty);
        set => SetValue(SelectionClearedCommandParameterProperty, value);
    }

    public static readonly DependencyProperty IsRefreshingProperty = DependencyProperty.Register(
        nameof(IsRefreshing), typeof(bool), typeof(ProductCategoryLookupTextBox), new PropertyMetadata(false));

    public bool IsRefreshing
    {
        get => (bool)GetValue(IsRefreshingProperty);
        set => SetValue(IsRefreshingProperty, value);
    }

    public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.Register(
        nameof(Placeholder), typeof(string), typeof(ProductCategoryLookupTextBox),
        new PropertyMetadata("搜索产品分类"));

    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    bool ILookupResultHost.HasItems => FilteredRoots.Count > 0;

    object? ILookupResultHost.CurrentItem => _currentNode?.Source;

    void ILookupResultHost.Refresh(IEnumerable? itemsSource, string query)
    {
        var roots = itemsSource?.OfType<ProductCategoryCacheNode>().ToArray() ?? [];
        var selectedId = SelectedCategoryId ?? SelectedCategory?.Uid;
        _filteredRoots.Clear();

        foreach (var root in roots)
        {
            var projection = BuildProjection(root, query, selectedId, out _);
            if (projection is not null)
            {
                _filteredRoots.Add(projection);
            }
        }

        _currentNode = Flatten(FilteredRoots).FirstOrDefault(node => node.Uid == selectedId)
                       ?? FlattenVisible().FirstOrDefault();
        if (_currentNode is not null)
        {
            _currentNode.IsSelected = true;
        }

        SynchronizeSelectedCategory(roots, selectedId);
        UpdateTreeExpansionActionText();
    }

    void ILookupResultHost.SelectFirst()
    {
        SelectNode(FlattenVisible().FirstOrDefault());
    }

    void ILookupResultHost.MoveSelection(int offset)
    {
        var visible = FlattenVisible().ToArray();
        if (visible.Length == 0)
        {
            return;
        }

        var currentIndex = _currentNode is null ? -1 : Array.IndexOf(visible, _currentNode);
        SelectNode(visible[Math.Clamp(currentIndex + offset, 0, visible.Length - 1)]);
    }

    bool ILookupResultHost.TryResolveExact(string text, out object? item)
    {
        item = FlattenSource(ItemsSource?.OfType<ProductCategoryCacheNode>() ?? [])
            .FirstOrDefault(node =>
                IsExact(node.CategoryName, text)
                || IsExact(node.CategoryCode, text)
                || IsExact(node.FakeName, text));
        return item is not null;
    }

    public void FocusEditor() => LookupEditor.FocusEditor();

    private static DependencyProperty RegisterCommand(string name) =>
        DependencyProperty.Register(name, typeof(ICommand), typeof(ProductCategoryLookupTextBox), new PropertyMetadata(null));

    private static DependencyProperty RegisterObject(string name) =>
        DependencyProperty.Register(name, typeof(object), typeof(ProductCategoryLookupTextBox), new PropertyMetadata(null));

    private static void OnSelectedCategoryChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (ProductCategoryLookupTextBox)d;
        if (control._isSynchronizingSelection)
        {
            return;
        }

        control._isSynchronizingSelection = true;
        try
        {
            control.SetCurrentValue(
                SelectedCategoryIdProperty,
                e.NewValue is ProductCategoryCacheNode node ? node.Uid : null);
        }
        finally
        {
            control._isSynchronizingSelection = false;
        }
    }

    private static void OnSelectedCategoryIdChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (ProductCategoryLookupTextBox)d;
        if (!control._isSynchronizingSelection)
        {
            ((ILookupResultHost)control).Refresh(control.ItemsSource, control.Text);
        }
    }

    private ProductCategoryLookupNode? BuildProjection(
        ProductCategoryCacheNode source,
        string query,
        Guid? selectedId,
        out bool containsSelected)
    {
        var children = new List<ProductCategoryLookupNode>();
        containsSelected = source.Uid == selectedId;
        foreach (var child in source.Children)
        {
            var childProjection = BuildProjection(child, query, selectedId, out var childContainsSelected);
            containsSelected |= childContainsSelected;
            if (childProjection is not null)
            {
                children.Add(childProjection);
            }
        }

        var hasQuery = !string.IsNullOrWhiteSpace(query);
        if (hasQuery && !Matches(source, query) && children.Count == 0)
        {
            return null;
        }

        var projection = new ProductCategoryLookupNode(source)
        {
            IsExpanded = hasQuery && children.Count > 0 || containsSelected,
            IsSelected = source.Uid == selectedId,
        };
        foreach (var child in children)
        {
            projection.Children.Add(child);
        }

        return projection;
    }

    private static bool Matches(ProductCategoryCacheNode node, string query) =>
        LookupTextMatcher.IsMatch(node.CategoryName, query)
        || LookupTextMatcher.IsMatch(node.CategoryCode, query)
        || LookupTextMatcher.IsMatch(node.FakeName, query);

    private static bool IsExact(string value, string text) =>
        string.Equals(value.Trim(), text.Trim(), StringComparison.CurrentCultureIgnoreCase);

    private static IEnumerable<ProductCategoryCacheNode> FlattenSource(IEnumerable<ProductCategoryCacheNode> roots)
    {
        foreach (var root in roots)
        {
            yield return root;
            foreach (var child in FlattenSource(root.Children))
            {
                yield return child;
            }
        }
    }

    private static IEnumerable<ProductCategoryLookupNode> Flatten(IEnumerable<ProductCategoryLookupNode> roots)
    {
        foreach (var root in roots)
        {
            yield return root;
            foreach (var child in Flatten(root.Children))
            {
                yield return child;
            }
        }
    }

    private IEnumerable<ProductCategoryLookupNode> FlattenVisible()
    {
        foreach (var root in FilteredRoots)
        {
            yield return root;
            if (!root.IsExpanded)
            {
                continue;
            }

            foreach (var child in FlattenVisible(root.Children))
            {
                yield return child;
            }
        }
    }

    private static IEnumerable<ProductCategoryLookupNode> FlattenVisible(IEnumerable<ProductCategoryLookupNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            if (!node.IsExpanded)
            {
                continue;
            }

            foreach (var child in FlattenVisible(node.Children))
            {
                yield return child;
            }
        }
    }

    private void SelectNode(ProductCategoryLookupNode? node)
    {
        if (node is null)
        {
            return;
        }

        if (_currentNode is not null)
        {
            _currentNode.IsSelected = false;
        }

        _currentNode = node;
        node.IsSelected = true;
    }

    private void SynchronizeSelectedCategory(IEnumerable<ProductCategoryCacheNode> roots, Guid? selectedId)
    {
        if (selectedId is null || selectedId == Guid.Empty)
        {
            return;
        }

        var resolved = FlattenSource(roots).FirstOrDefault(node => node.Uid == selectedId);
        if (ReferenceEquals(resolved, SelectedCategory))
        {
            return;
        }

        _isSynchronizingSelection = true;
        try
        {
            SetCurrentValue(SelectedCategoryProperty, resolved);
        }
        finally
        {
            _isSynchronizingSelection = false;
        }
    }

    private void CategoryTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        _currentNode = e.NewValue as ProductCategoryLookupNode;
    }

    private void CategoryTree_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source)
        {
            return;
        }

        if (FindVisualAncestor<ToggleButton>(source) is not null)
        {
            return;
        }

        var item = FindVisualAncestor<TreeViewItem>(source);
        if (item?.DataContext is not ProductCategoryLookupNode node)
        {
            return;
        }

        SelectNode(node);
        LookupEditor.CommitExternalSelection(node.Source, LookupCommitReason.Mouse);
        e.Handled = true;
    }

    private void ToggleTreeExpansion_Click(object sender, RoutedEventArgs e)
    {
        var branches = Flatten(FilteredRoots)
            .Where(node => node.Children.Count > 0)
            .ToArray();
        if (branches.Length == 0)
        {
            return;
        }

        var expand = branches.Any(node => !node.IsExpanded);
        foreach (var branch in branches)
        {
            branch.IsExpanded = expand;
        }

        UpdateTreeExpansionActionText();
    }

    private void CategoryTree_ExpansionChanged(object sender, RoutedEventArgs e) =>
        UpdateTreeExpansionActionText();

    private void UpdateTreeExpansionActionText()
    {
        var branches = Flatten(FilteredRoots)
            .Where(node => node.Children.Count > 0)
            .ToArray();
        TreeExpansionActionText = branches.Length > 0 && branches.All(node => node.IsExpanded)
            ? "折叠全部"
            : "展开全部";
    }

    private static T? FindVisualAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T ancestor)
            {
                return ancestor;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }
}
