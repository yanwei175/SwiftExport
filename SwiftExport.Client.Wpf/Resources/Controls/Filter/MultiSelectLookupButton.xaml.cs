using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace SwiftExportClient.Resources.Controls.Filter;

public partial class MultiSelectLookupButton : UserControl
{
    private readonly ObservableCollection<LookupItem> _lookupItems = new();

    private ICollectionView? _itemsView;


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
            new PropertyMetadata(null));

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    #endregion

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

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        SubscribeSelectedItems(
            SelectedItems as INotifyCollectionChanged);


        UpdateDisplayText();
    }

    private void OnUnloaded(
        object sender,
        RoutedEventArgs e)
    {
        UnsubscribeSelectedItems(
            SelectedItems as INotifyCollectionChanged);
    }

    #endregion

    #region Popup

    private void Popup_OnOpened(object? sender, EventArgs e)
    {
        BuildLookupItems();

        PART_SearchBox.Text = string.Empty;

        _itemsView =
            CollectionViewSource.GetDefaultView(_lookupItems);

        _itemsView.Filter = FilterItem;

        PART_ListBox.ItemsSource = _itemsView;

        if (ShowSearchBox)
        {
            PART_SearchBox.Focus();
            PART_SearchBox.SelectAll();
        }
    }

    private void Popup_OnClosed(object? sender, EventArgs e)
    {
        // Popup 关闭时丢弃尚未 Commit 的临时状态。
        PART_SearchBox.Text = string.Empty;

        PART_ListBox.ItemsSource = null;

        _itemsView = null;

        _lookupItems.Clear();
    }

    #endregion

    #region Lookup Items

    private void BuildLookupItems()
    {
        _lookupItems.Clear();

        if (ItemsSource == null)
            return;

        foreach (var item in ItemsSource)
        {
            if (item == null)
                continue;

            var value = GetMemberValue(
                item,
                SelectedValuePath);

            var displayText = GetDisplayText(item);

            _lookupItems.Add(new LookupItem
            {
                Item = item,

                Value = value,

                DisplayText = displayText,

                // 有外部模板时，把原始 DTO 给模板。
                // 没有模板时，直接显示 DisplayMemberPath 得到的字符串。
                DisplayContent = ItemTemplate != null
                    ? item
                    : displayText,

                ContentTemplate = ItemTemplate,

                IsChecked = IsItemSelected(
                    item,
                    value)
            });
        }
    }

    private void SyncLookupItemsFromSelectedItems()
    {
        if (_lookupItems.Count == 0)
            return;

        foreach (var lookupItem in _lookupItems)
        {
            lookupItem.IsChecked =
                IsItemSelected(
                    lookupItem.Item,
                    lookupItem.Value);
        }
    }

    private bool IsItemSelected(
        object item,
        object? value)
    {
        if (SelectedItems == null)
            return false;

        foreach (var selectedItem in SelectedItems)
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

    private void SearchBox_OnTextChanged(
        object sender,
        TextChangedEventArgs e)
    {
        _itemsView?.Refresh();
    }

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

        return false;
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

    private void ResetButton_OnClick(object sender, RoutedEventArgs e)
    {
        // 只清除 Popup 临时状态。
        // 点击确定以后才真正写回 SelectedItems。
        foreach (var item in _lookupItems)
        {
            item.IsChecked = false;
        }
    }

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
        var selectedItems = _lookupItems
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

    private sealed class LookupItem : INotifyPropertyChanged
    {
        private bool _isChecked;

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

        public bool IsChecked
        {
            get => _isChecked;

            set
            {
                if (_isChecked == value)
                    return;

                _isChecked = value;

                PropertyChanged?.Invoke(
                    this,
                    new PropertyChangedEventArgs(
                        nameof(IsChecked)));
            }
        }

        public event PropertyChangedEventHandler?
            PropertyChanged;
    }

    #endregion
    private void PART_ListBox_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListBox listBox)
            return;

        var source = e.OriginalSource as DependencyObject;

        var itemContainer = ItemsControl.ContainerFromElement(
            listBox,
            source) as ListBoxItem;

        if (itemContainer?.DataContext is not LookupItem item)
            return;

        item.IsChecked = !item.IsChecked;

        e.Handled = true;
    }
}