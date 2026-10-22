using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Controls.Primitives;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SwiftExportClient.Resources.Controls.Filter;

/// <summary>以弹出层暂存多选，支持平面或树形本地候选，确定后才写回绑定集合。</summary>
public partial class MultiSelectLookupButton : UserControl
{
    private readonly ObservableCollection<LookupItem> _lookupItems = new();

    private ICollectionView? _itemsView;
    private INotifyCollectionChanged? _itemsSourceNotifier;


    /// <summary>初始化多选界面及加载、卸载时的集合订阅。</summary>
    public MultiSelectLookupButton()
    {
        InitializeComponent();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    #region Header

    public static readonly DependencyProperty HeaderProperty =
        DependencyProperty.Register(
            nameof(Header),
            typeof(string),
            typeof(MultiSelectLookupButton),
            new PropertyMetadata(
                string.Empty,
                OnHeaderChanged));

    public string Header
    {
        get => (string)GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    private static void OnHeaderChanged(
        DependencyObject d,
        DependencyPropertyChangedEventArgs e)
    {
        ((MultiSelectLookupButton)d).UpdateDisplayText();
    }

    #endregion

    #region ItemsSource

    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(
            nameof(ItemsSource),
            typeof(IEnumerable),
            typeof(MultiSelectLookupButton),
            new PropertyMetadata(null, OnItemsSourceChanged));

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    #endregion

    /// <summary>标识子集合属性路径；非空时以树形展示候选。</summary>
    public static readonly DependencyProperty ChildrenMemberPathProperty = DependencyProperty.Register(
        nameof(ChildrenMemberPath), typeof(string), typeof(MultiSelectLookupButton), new PropertyMetadata(string.Empty));

    /// <summary>获取或设置子集合路径；各节点独立勾选，不联动父子选择。</summary>
    public string ChildrenMemberPath
    {
        get => (string)GetValue(ChildrenMemberPathProperty);
        set => SetValue(ChildrenMemberPathProperty, value);
    }

    #region SelectedItems

    public static readonly DependencyProperty SelectedItemsProperty =
        DependencyProperty.Register(
            nameof(SelectedItems),
            typeof(IList),
            typeof(MultiSelectLookupButton),
            new FrameworkPropertyMetadata(
                null,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnSelectedItemsChanged));

    /// <summary>
    /// 获取或设置已提交的完整对象集合。
    /// 外部修改此集合时，控件会同步更新勾选状态和按钮显示数量。
    /// </summary>
    public IList? SelectedItems
    {
        get => (IList?)GetValue(SelectedItemsProperty);
        set => SetValue(SelectedItemsProperty, value);
    }

    private static void OnSelectedItemsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (MultiSelectLookupButton)d;

        control.UnsubscribeSelectedItems(
            e.OldValue as INotifyCollectionChanged);

        control.SubscribeSelectedItems(
            e.NewValue as INotifyCollectionChanged);

        control.SyncLookupItemsFromSelectedItems();
        control.UpdateDisplayText();
    }

    #endregion


    #region DisplayMemberPath

    public static readonly DependencyProperty DisplayMemberPathProperty =
        DependencyProperty.Register(
            nameof(DisplayMemberPath),
            typeof(string),
            typeof(MultiSelectLookupButton),
            new PropertyMetadata(string.Empty));

    public string DisplayMemberPath
    {
        get => (string)GetValue(DisplayMemberPathProperty);
        set => SetValue(DisplayMemberPathProperty, value);
    }

    #endregion

    #region FilterMemberPaths

    public static readonly DependencyProperty FilterMemberPathsProperty =
        DependencyProperty.Register(
            nameof(FilterMemberPaths),
            typeof(string),
            typeof(MultiSelectLookupButton),
            new PropertyMetadata(string.Empty));

    /// <summary>
    /// 获取或设置参与本地搜索的属性路径。
    /// 多个字段使用逗号分隔。
    /// </summary>
    public string FilterMemberPaths
    {
        get => (string)GetValue(FilterMemberPathsProperty);
        set => SetValue(FilterMemberPathsProperty, value);
    }

    #endregion

    #region SelectedValuePath

    public static readonly DependencyProperty SelectedValuePathProperty =
        DependencyProperty.Register(
            nameof(SelectedValuePath),
            typeof(string),
            typeof(MultiSelectLookupButton),
            new PropertyMetadata(string.Empty));

    /// <summary>
    /// 获取或设置用于识别选项唯一性的属性路径。
    /// 当 ItemsSource 中的对象实例发生变化时，控件可通过该属性判断是否为同一选项。
    /// 例如 Uid。
    /// </summary>
    public string SelectedValuePath
    {
        get => (string)GetValue(SelectedValuePathProperty);
        set => SetValue(SelectedValuePathProperty, value);
    }

    #endregion

    #region ItemTemplate

    public static readonly DependencyProperty ItemTemplateProperty =
        DependencyProperty.Register(
            nameof(ItemTemplate),
            typeof(DataTemplate),
            typeof(MultiSelectLookupButton),
            new PropertyMetadata(null));

    /// <summary>
    /// 获取或设置选项显示模板。
    /// DataContext 为 ItemsSource 中原始业务对象。
    /// </summary>
    public DataTemplate? ItemTemplate
    {
        get => (DataTemplate?)GetValue(ItemTemplateProperty);
        set => SetValue(ItemTemplateProperty, value);
    }

    #endregion

    #region Placeholder

    public static readonly DependencyProperty PlaceholderProperty =
        DependencyProperty.Register(nameof(Placeholder), typeof(string), typeof(MultiSelectLookupButton), new PropertyMetadata("搜索"));

    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    #endregion

    #region ShowSearchBox

    public static readonly DependencyProperty ShowSearchBoxProperty =
        DependencyProperty.Register(
            nameof(ShowSearchBox),
            typeof(bool),
            typeof(MultiSelectLookupButton),
            new PropertyMetadata(true));

    public bool ShowSearchBox
    {
        get => (bool)GetValue(ShowSearchBoxProperty);
        set => SetValue(ShowSearchBoxProperty, value);
    }

    #endregion

    #region SelectionCommittedCommand

    public static readonly DependencyProperty SelectionCommittedCommandProperty =
        DependencyProperty.Register(
            nameof(SelectionCommittedCommand),
            typeof(ICommand),
            typeof(MultiSelectLookupButton));

    public ICommand? SelectionCommittedCommand
    {
        get => (ICommand?)GetValue(SelectionCommittedCommandProperty);
        set => SetValue(SelectionCommittedCommandProperty, value);
    }

    #endregion

    #region SelectionCommittedCommandParameter

    public static readonly DependencyProperty SelectionCommittedCommandParameterProperty =
        DependencyProperty.Register(
            nameof(SelectionCommittedCommandParameter),
            typeof(object),
            typeof(MultiSelectLookupButton));

    public object? SelectionCommittedCommandParameter
    {
        get => GetValue(SelectionCommittedCommandParameterProperty);
        set => SetValue(SelectionCommittedCommandParameterProperty, value);
    }

    #endregion

    #region DisplayText

    private static readonly DependencyPropertyKey DisplayTextPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(DisplayText),
            typeof(string),
            typeof(MultiSelectLookupButton),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty DisplayTextProperty =
        DisplayTextPropertyKey.DependencyProperty;

    /// <summary>
    /// 获取按钮显示文字，例如“颜色(2)”。
    /// </summary>
    public string DisplayText
    {
        get => (string)GetValue(DisplayTextProperty);
        private set => SetValue(DisplayTextPropertyKey, value);
    }

    #endregion

    #region Lifecycle

    /// <summary>进入视觉树时恢复候选与外部已选集合的通知订阅。</summary>
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        AttachItemsSourceNotifier();
        SubscribeSelectedItems(
            SelectedItems as INotifyCollectionChanged);


        UpdateDisplayText();
    }

    /// <summary>离开视觉树时取消弹层草稿并解除外部集合订阅。</summary>
    private void OnUnloaded(
        object sender,
        RoutedEventArgs e)
    {
        PART_Popup.IsOpen = false;
        DetachItemsSourceNotifier();
        UnsubscribeSelectedItems(
            SelectedItems as INotifyCollectionChanged);
    }

    /// <summary>切换源时重接通知；树形弹层正在编辑时保留仍存在的临时选择。</summary>
    private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (MultiSelectLookupButton)d;
        control.DetachItemsSourceNotifier();
        control.AttachItemsSourceNotifier();
        control.RefreshOpenTree();
    }

    /// <summary>仅在已加载期间监听缓存根集合，卸载后不保留控件引用。</summary>
    private void AttachItemsSourceNotifier()
    {
        if (!IsLoaded || _itemsSourceNotifier is not null || ItemsSource is not INotifyCollectionChanged notifier)
            return;
        _itemsSourceNotifier = notifier;
        notifier.CollectionChanged += OnItemsSourceCollectionChanged;
    }

    /// <summary>解除旧候选集合通知，允许控件卸载和重复加载。</summary>
    private void DetachItemsSourceNotifier()
    {
        if (_itemsSourceNotifier is not null)
            _itemsSourceNotifier.CollectionChanged -= OnItemsSourceCollectionChanged;
        _itemsSourceNotifier = null;
    }

    /// <summary>接纳缓存刷新，不提前提交多选草稿。</summary>
    private void OnItemsSourceCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => RefreshOpenTree();

    /// <summary>按稳定值匹配新节点，替换已打开树中的过期缓存实例。</summary>
    private void RefreshOpenTree()
    {
        if (!PART_Popup.IsOpen || string.IsNullOrWhiteSpace(ChildrenMemberPath))
            return;
        var draft = FlattenItems(_lookupItems).Where(item => item.IsChecked).Select(item => item.Item).ToArray();
        BuildLookupItems(draft);
        RefreshFilters();
    }

    #endregion

    #region Popup

    /// <summary>打开时从当前源创建选择草稿，并按显示模式连接列表或树视图。</summary>
    private void Popup_OnOpened(object? sender, EventArgs e)
    {
        BuildLookupItems();

        PART_SearchBox.Text = string.Empty;

        _itemsView =
            CollectionViewSource.GetDefaultView(_lookupItems);

        _itemsView.Filter = FilterItem;

        PART_ListBox.ItemsSource = _itemsView;
        PART_TreeView.ItemsSource = _itemsView;
        RefreshFilters();

        if (ShowSearchBox)
        {
            PART_SearchBox.Focus();
            PART_SearchBox.SelectAll();
        }
    }

    /// <summary>关闭时清理视图和临时投影；未点确定的勾选不会写回。</summary>
    private void Popup_OnClosed(object? sender, EventArgs e)
    {
        // Popup 关闭时丢弃尚未 Commit 的临时状态。
        PART_SearchBox.Text = string.Empty;

        PART_ListBox.ItemsSource = null;
        PART_TreeView.ItemsSource = null;

        _itemsView = null;

        _lookupItems.Clear();
    }

    #endregion

    #region Lookup Items

    /// <summary>从只读业务对象创建独立勾选投影；草稿参数用于缓存刷新时恢复选择。</summary>
    private void BuildLookupItems(IEnumerable? draft = null)
    {
        _lookupItems.Clear();

        if (ItemsSource == null)
            return;

        foreach (var item in ItemsSource)
        {
            if (item == null)
                continue;

            _lookupItems.Add(BuildLookupItem(item, draft));
        }
    }

    /// <summary>递归投影子集合，仅保存界面状态，不向缓存节点写入勾选或展开状态。</summary>
    private LookupItem BuildLookupItem(object item, IEnumerable? draft)
    {
        var value = GetMemberValue(item, SelectedValuePath);
        var displayText = GetDisplayText(item);
        var result = new LookupItem
        {
            Item = item, Value = value, DisplayText = displayText,
            DisplayContent = ItemTemplate is null ? displayText : item,
            ContentTemplate = ItemTemplate,
            IsChecked = IsItemSelected(item, value, draft),
        };
        if (!string.IsNullOrWhiteSpace(ChildrenMemberPath)
            && GetMemberValue(item, ChildrenMemberPath) is IEnumerable children)
        {
            foreach (var child in children)
                if (child is not null) result.Children.Add(BuildLookupItem(child, draft));
        }
        return result;
    }

    /// <summary>遍历完整投影，搜索隐藏的节点仍参与清除和最终提交。</summary>
    private static IEnumerable<LookupItem> FlattenItems(IEnumerable<LookupItem> items)
    {
        foreach (var item in items)
        {
            yield return item;
            foreach (var child in FlattenItems(item.Children)) yield return child;
        }
    }

    /// <summary>外部已选集合变化时同步全部层级的勾选，不发出提交命令。</summary>
    private void SyncLookupItemsFromSelectedItems()
    {
        if (_lookupItems.Count == 0)
            return;

        foreach (var lookupItem in FlattenItems(_lookupItems))
        {
            lookupItem.IsChecked =
                IsItemSelected(
                    lookupItem.Item,
                    lookupItem.Value);
        }
    }

    /// <summary>优先按稳定值匹配分类刷新前后的对象；未配置值路径时按对象匹配。</summary>
    private bool IsItemSelected(
        object item,
        object? value,
        IEnumerable? selection = null)
    {
        selection ??= SelectedItems;
        if (selection == null)
            return false;

        foreach (var selectedItem in selection)
        {
            if (selectedItem == null)
                continue;

            // 有 SelectedValuePath 时，优先按 Value 判断。
            if (!string.IsNullOrWhiteSpace(
                    SelectedValuePath))
            {
                var selectedValue =
                    GetMemberValue(
                        selectedItem,
                        SelectedValuePath);

                if (Equals(
                        selectedValue,
                        value))
                {
                    return true;
                }
            }
            else
            {
                // 没有 SelectedValuePath 时按对象判断。
                if (Equals(
                        selectedItem,
                        item))
                {
                    return true;
                }
            }
        }

        return false;
    }

    #endregion

    #region Search

    /// <summary>本地关键词变化只刷新候选显示，不修改已选状态或触发远程请求。</summary>
    private void SearchBox_OnTextChanged(
        object sender,
        TextChangedEventArgs e)
    {
        RefreshFilters();
    }

    /// <summary>按层刷新本地搜索，命中后代时保留并展开祖先，原始候选及勾选不变。</summary>
    private void RefreshFilters()
    {
        foreach (var item in FlattenItems(_lookupItems))
        {
            item.ChildrenView.Filter = FilterItem;
            item.ChildrenView.Refresh();
            if (!string.IsNullOrWhiteSpace(PART_SearchBox.Text)) item.IsExpanded = true;
        }
        _itemsView?.Refresh();
    }

    /// <summary>匹配配置字段或后代；未配置字段的枚举按描述和名称搜索，保留命中分支的祖先。</summary>
    private bool FilterItem(
        object item)
    {
        if (item is not LookupItem lookupItem)
            return false;

        var searchText =
            PART_SearchBox.Text?.Trim();

        if (string.IsNullOrWhiteSpace(searchText))
            return true;

        var paths = GetFilterPaths();

        // 枚举本身没有属性路径，中文模板的描述仍应能被用户搜索到。
        if (paths.Count == 0 && lookupItem.Item is Enum enumValue)
        {
            var name = enumValue.ToString();
            var description = enumValue.GetType().GetField(name)?.GetCustomAttribute<DescriptionAttribute>()?.Description;
            if (name.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0
                || description?.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }

        foreach (var path in paths)
        {
            var value =
                GetMemberValue(
                    lookupItem.Item,
                    path);

            if (value == null)
                continue;

            var text = value.ToString();

            if (string.IsNullOrWhiteSpace(text))
                continue;

            if (text.IndexOf(
                    searchText,
                    StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return lookupItem.Children.Any(child => FilterItem(child));
    }

    private IReadOnlyList<string> GetFilterPaths()
    {
        if (!string.IsNullOrWhiteSpace(
                FilterMemberPaths))
        {
            return FilterMemberPaths
                .Split(',')
                .Select(x => x.Trim())
                .Where(x =>
                    !string.IsNullOrWhiteSpace(x))
                .ToArray();
        }

        if (!string.IsNullOrWhiteSpace(
                DisplayMemberPath))
        {
            return new[]
            {
                DisplayMemberPath
            };
        }

        return Array.Empty<string>();
    }

    #endregion

    #region Apply / Clear

    private void ApplyButton_OnClick(object sender, RoutedEventArgs e)
    {
        CommitSelection();

        PART_Popup.IsOpen = false;
    }

    /// <summary>清除完整草稿，包含搜索隐藏节点；最终选择仍需确定后更新。</summary>
    private void ResetButton_OnClick(object sender, RoutedEventArgs e)
    {
        // 只清除 Popup 临时状态。
        // 点击确定以后才真正写回 SelectedItems。
        foreach (var item in FlattenItems(_lookupItems))
        {
            item.IsChecked = false;
        }
    }

    /// <summary>冻结所有层级的勾选后更新完整对象集合，最后执行一次提交命令。</summary>
    private void CommitSelection()
    {
        if (SelectedItems == null)
            return;

        if (SelectedItems.IsReadOnly ||
            SelectedItems.IsFixedSize)
        {
            throw new InvalidOperationException(
                $"{nameof(SelectedItems)} 必须是可修改集合。");
        }

        // 必须先保存用户在 Popup 中的临时选择。
        // SelectedItems.Clear() 会触发 CollectionChanged，
        // 从而重新同步 _lookupItems.IsChecked。
        var selectedItems = FlattenItems(_lookupItems)
            .Where(x => x.IsChecked)
            .Select(x => x.Item)
            .ToArray();

        SelectedItems.Clear();

        foreach (var item in selectedItems)
        {
            SelectedItems.Add(item);
        }

        UpdateDisplayText();

        //var parameter =
        //    SelectionCommittedCommandParameter
        //    ?? SelectedItems;
        var parameter = SelectionCommittedCommandParameter;

        if (SelectionCommittedCommand?.CanExecute(parameter) == true)
        {
            SelectionCommittedCommand.Execute(parameter);
        }
    }

    #endregion

    #region SelectedItems Synchronization

    private void SubscribeSelectedItems(
        INotifyCollectionChanged? collection)
    {
        if (collection == null)
            return;

        // 防止 Loaded / DP Changed 重复注册。
        collection.CollectionChanged -=
            SelectedItems_CollectionChanged;

        collection.CollectionChanged +=
            SelectedItems_CollectionChanged;
    }

    private void UnsubscribeSelectedItems(
        INotifyCollectionChanged? collection)
    {
        if (collection == null)
            return;

        collection.CollectionChanged -=
            SelectedItems_CollectionChanged;
    }

    private void SelectedItems_CollectionChanged(
        object? sender,
        NotifyCollectionChangedEventArgs e)
    {
        SyncLookupItemsFromSelectedItems();
        UpdateDisplayText();
    }


    #endregion

    #region Display

    private void UpdateDisplayText()
    {
        var count =
            SelectedItems?.Count ?? 0;

        DisplayText =
            count == 0
                ? Header ?? string.Empty
                : $"{Header}({count})";
    }

    private string GetDisplayText(
        object item)
    {
        if (string.IsNullOrWhiteSpace(
                DisplayMemberPath))
        {
            return item.ToString()
                   ?? string.Empty;
        }

        return GetMemberValue(
                   item,
                   DisplayMemberPath)
               ?.ToString()
               ?? string.Empty;
    }

    #endregion

    #region Reflection

    /// <summary>
    /// 获取属性路径值。
    /// 支持 Uid、Supplier.Name 等路径。
    /// </summary>
    private static object? GetMemberValue(
        object? source,
        string? memberPath)
    {
        if (source == null)
            return null;

        if (string.IsNullOrWhiteSpace(
                memberPath))
        {
            return source;
        }

        object? current = source;

        foreach (var memberName in
                 memberPath.Split('.'))
        {
            if (current == null)
                return null;

            var property =
                current.GetType().GetProperty(
                    memberName,
                    BindingFlags.Instance |
                    BindingFlags.Public);

            if (property == null)
                return null;

            current =
                property.GetValue(current);
        }

        return current;
    }

    #endregion

    #region Internal Item

    /// <summary>承载多选弹层的临时状态，不修改原始业务对象。</summary>
    private sealed partial class LookupItem : ObservableObject
    {
        /// <summary>创建独立子集合视图，筛选不影响共享数据。</summary>
        public LookupItem() => ChildrenView = new ListCollectionView(Children);

        /// <summary>获取子节点投影。</summary>
        public ObservableCollection<LookupItem> Children { get; } = [];

        /// <summary>获取保留祖先关系的子候选视图。</summary>
        public ICollectionView ChildrenView { get; }

        /// <summary>获取或设置当前分支展开状态。</summary>
        [ObservableProperty]
        public partial bool IsExpanded { get; set; } = true;

        public required object Item { get; init; }

        public object? Value { get; init; }

        public required string DisplayText
        {
            get;
            init;
        }

        public required object DisplayContent
        {
            get;
            init;
        }

        public DataTemplate? ContentTemplate
        {
            get;
            init;
        }

        /// <summary>获取或设置该节点自身的临时勾选，不联动后代。</summary>
        [ObservableProperty]
        public partial bool IsChecked { get; set; }
    }

    #endregion

    /// <summary>仅点击节点正文时切换该项，展开箭头继续由原生 TreeView 处理。</summary>
    private void TreeView_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source) return;
        for (var current = source; current is not null; current = System.Windows.Media.VisualTreeHelper.GetParent(current))
        {
            if (current is ToggleButton) return;
            if (current is TreeViewItem { DataContext: LookupItem item })
            {
                item.IsChecked = !item.IsChecked;
                e.Handled = true;
                return;
            }
        }
    }

    /// <summary>空格切换当前分类；方向键和展开折叠继续使用原生树导航。</summary>
    private void TreeView_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space && PART_TreeView.SelectedItem is LookupItem item)
        {
            item.IsChecked = !item.IsChecked;
            e.Handled = true;
        }
    }

    /// <summary>未被子编辑器处理的 Esc 取消草稿，Enter 确定；关闭后返回入口焦点。</summary>
    private void PopupContent_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Escape or Key.Enter)) return;
        if (e.Key == Key.Enter) CommitSelection();
        PART_Popup.IsOpen = false;
        PART_Button.Focus();
        e.Handled = true;
    }
}
