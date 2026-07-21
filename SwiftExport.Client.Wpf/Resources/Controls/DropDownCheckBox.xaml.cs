using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SwiftExportClient.Resources.Controls;

/// <summary>
/// 通过下拉复选框列表提供多选能力，并以一基序号显示当前选择。
/// </summary>
public partial class DropDownCheckBox : UserControl
{
    private static readonly DependencyPropertyKey SelectionTextPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(SelectionText),
            typeof(string),
            typeof(DropDownCheckBox),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty SelectionTextProperty = SelectionTextPropertyKey.DependencyProperty;

    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(
            nameof(ItemsSource),
            typeof(IEnumerable),
            typeof(DropDownCheckBox),
            new PropertyMetadata(null, OnItemsSourceChanged));

    public static readonly DependencyProperty SelectedItemsProperty =
        DependencyProperty.Register(
            nameof(SelectedItems),
            typeof(IList),
            typeof(DropDownCheckBox),
            new FrameworkPropertyMetadata(
                null,
                FrameworkPropertyMetadataOptions.None,
                OnSelectedItemsChanged));

    public static readonly DependencyProperty PlaceholderProperty =
        DependencyProperty.Register(
            nameof(Placeholder),
            typeof(string),
            typeof(DropDownCheckBox),
            new PropertyMetadata("请选择", OnPlaceholderChanged));

    public static readonly DependencyProperty DisplayMemberPathProperty =
        DependencyProperty.Register(
            nameof(DisplayMemberPath),
            typeof(string),
            typeof(DropDownCheckBox),
            new PropertyMetadata(string.Empty, OnDisplayMemberPathChanged));

    public static readonly DependencyProperty MaxDropDownHeightProperty =
        DependencyProperty.Register(
            nameof(MaxDropDownHeight),
            typeof(double),
            typeof(DropDownCheckBox),
            new FrameworkPropertyMetadata(280d, null, CoerceMaxDropDownHeight));

    public static readonly DependencyProperty IsDropDownOpenProperty =
        DependencyProperty.Register(
            nameof(IsDropDownOpen),
            typeof(bool),
            typeof(DropDownCheckBox),
            new FrameworkPropertyMetadata(
                false,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty ShowClearOptionProperty =
        DependencyProperty.Register(
            nameof(ShowClearOption),
            typeof(bool),
            typeof(DropDownCheckBox),
            new PropertyMetadata(true));

    private readonly ObservableCollection<DropDownCheckBoxOption> _options = [];
    private INotifyCollectionChanged? _itemsSourceNotifier;
    private INotifyCollectionChanged? _selectedItemsNotifier;
    private bool _isSynchronizing;

    public DropDownCheckBox()
    {
        InitializeComponent();
        OptionItemsControl.ItemsSource = _options;
        SetCurrentValue(SelectedItemsProperty, new ObservableCollection<object?>());
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        UpdateSelectionText();
    }

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    /// <summary>
    /// 获取或设置外部可写的选中项集合。集合成员始终是 ItemsSource 中的原始对象。
    /// </summary>
    public IList? SelectedItems
    {
        get => (IList?)GetValue(SelectedItemsProperty);
        set => SetValue(SelectedItemsProperty, value);
    }

    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    public string DisplayMemberPath
    {
        get => (string)GetValue(DisplayMemberPathProperty);
        set => SetValue(DisplayMemberPathProperty, value);
    }

    public double MaxDropDownHeight
    {
        get => (double)GetValue(MaxDropDownHeightProperty);
        set => SetValue(MaxDropDownHeightProperty, value);
    }

    /// <summary>
    /// 获取或设置下拉复选列表是否打开。默认支持双向绑定。
    /// </summary>
    public bool IsDropDownOpen
    {
        get => (bool)GetValue(IsDropDownOpenProperty);
        set => SetValue(IsDropDownOpenProperty, value);
    }

    /// <summary>
    /// 获取或设置是否在下拉列表顶部显示“清空选择”操作。默认显示。
    /// </summary>
    public bool ShowClearOption
    {
        get => (bool)GetValue(ShowClearOptionProperty);
        set => SetValue(ShowClearOptionProperty, value);
    }

    public string SelectionText => (string)GetValue(SelectionTextProperty);

    private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (DropDownCheckBox)d;
        control.DetachItemsSourceNotifier();
        control.AttachItemsSourceNotifier();
        control.RebuildOptions();
    }

    private static void OnSelectedItemsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (DropDownCheckBox)d;
        control.DetachSelectedItemsNotifier();
        control.AttachSelectedItemsNotifier();
        if (!control._isSynchronizing)
        {
            control.SynchronizeOptionsFromSelection();
        }
    }

    private static void OnPlaceholderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((DropDownCheckBox)d).UpdateSelectionText();
    }

    private static void OnDisplayMemberPathChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((DropDownCheckBox)d).RebuildOptions();
    }

    private static object CoerceMaxDropDownHeight(DependencyObject d, object baseValue)
    {
        var value = (double)baseValue;
        return double.IsNaN(value) || value <= 0 ? 280d : value;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        AttachItemsSourceNotifier();
        AttachSelectedItemsNotifier();
        RebuildOptions();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        SetCurrentValue(IsDropDownOpenProperty, false);
        DetachItemsSourceNotifier();
        DetachSelectedItemsNotifier();
    }

    private void DropDownButton_Click(object sender, RoutedEventArgs e)
    {
        SetCurrentValue(IsDropDownOpenProperty, !IsDropDownOpen);
    }

    private void DropDownButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!IsDropDownOpen)
        {
            return;
        }

        // Popup 使用 StaysOpen=False。显式吞掉触发按钮的第二次点击，
        // 避免 Popup 自动关闭后 Button.Click 又立即把它重新打开。
        SetCurrentValue(IsDropDownOpenProperty, false);
        DropDownButton.Focus();
        e.Handled = true;
    }

    private void DropDownPopup_Opened(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Input, FocusInitialOption);
    }

    private void DropDownPopup_Closed(object? sender, EventArgs e)
    {
        if (IsDropDownOpen)
        {
            SetCurrentValue(IsDropDownOpenProperty, false);
        }
    }

    private void PopupSurface_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
        {
            return;
        }

        SetCurrentValue(IsDropDownOpenProperty, false);
        DropDownButton.Focus();
        e.Handled = true;
    }

    private void ClearSelectionButton_Click(object sender, RoutedEventArgs e)
    {
        var selectedItems = SelectedItems;
        if (!CanClearSelection(selectedItems))
        {
            UpdateClearOptionState();
            return;
        }

        try
        {
            selectedItems!.Clear();
        }
        catch (Exception exception) when (exception is NotSupportedException or InvalidOperationException)
        {
            // 外部集合可能仅在运行时拒绝修改；重新同步 UI，且不替换绑定集合。
        }

        SynchronizeOptionsFromSelection();
    }

    private void FocusInitialOption()
    {
        if (!IsDropDownOpen || !IsLoaded || _options.Count == 0)
        {
            return;
        }

        var targetIndex = 0;
        for (var index = 0; index < _options.Count; index++)
        {
            if (_options[index].IsSelected)
            {
                targetIndex = index;
                break;
            }
        }

        var container = OptionItemsControl.ItemContainerGenerator.ContainerFromIndex(targetIndex) as DependencyObject;
        var checkBox = container is null ? null : FindVisualDescendant<CheckBox>(container);
        checkBox?.Focus();
    }

    private static T? FindVisualDescendant<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
            {
                return match;
            }

            var nested = FindVisualDescendant<T>(child);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    private void AttachItemsSourceNotifier()
    {
        if (!IsLoaded || _itemsSourceNotifier is not null || ItemsSource is not INotifyCollectionChanged notifier)
        {
            return;
        }

        _itemsSourceNotifier = notifier;
        _itemsSourceNotifier.CollectionChanged += OnItemsSourceCollectionChanged;
    }

    private void DetachItemsSourceNotifier()
    {
        if (_itemsSourceNotifier is null)
        {
            return;
        }

        _itemsSourceNotifier.CollectionChanged -= OnItemsSourceCollectionChanged;
        _itemsSourceNotifier = null;
    }

    private void AttachSelectedItemsNotifier()
    {
        if (!IsLoaded || _selectedItemsNotifier is not null || SelectedItems is not INotifyCollectionChanged notifier)
        {
            return;
        }

        _selectedItemsNotifier = notifier;
        _selectedItemsNotifier.CollectionChanged += OnSelectedItemsCollectionChanged;
    }

    private void DetachSelectedItemsNotifier()
    {
        if (_selectedItemsNotifier is null)
        {
            return;
        }

        _selectedItemsNotifier.CollectionChanged -= OnSelectedItemsCollectionChanged;
        _selectedItemsNotifier = null;
    }

    private void OnItemsSourceCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RebuildOptions();
    }

    private void OnSelectedItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (!_isSynchronizing)
        {
            SynchronizeOptionsFromSelection();
        }
    }

    private void RebuildOptions()
    {
        _isSynchronizing = true;
        try
        {
            _options.Clear();
            if (ItemsSource is null)
            {
                return;
            }

            var ordinal = 1;
            foreach (var item in ItemsSource)
            {
                _options.Add(new DropDownCheckBoxOption(
                    this,
                    item,
                    ordinal++,
                    ResolveDisplayText(item),
                    ContainsSelectedItem(item)));
            }
        }
        finally
        {
            _isSynchronizing = false;
            UpdateSelectionText();
        }
    }

    private void SynchronizeOptionsFromSelection()
    {
        _isSynchronizing = true;
        try
        {
            foreach (var option in _options)
            {
                option.IsSelected = ContainsSelectedItem(option.Item);
            }
        }
        finally
        {
            _isSynchronizing = false;
            UpdateSelectionText();
        }
    }

    private void OnOptionSelectionChanged(DropDownCheckBoxOption option, bool isSelected)
    {
        if (_isSynchronizing)
        {
            return;
        }

        var selectedItems = EnsureSelectedItems();
        if (selectedItems.IsReadOnly || selectedItems.IsFixedSize)
        {
            SynchronizeOptionsFromSelection();
            return;
        }

        _isSynchronizing = true;
        try
        {
            var existingIndex = IndexOf(selectedItems, option.Item);
            if (isSelected && existingIndex < 0)
            {
                selectedItems.Add(option.Item);
            }
            else if (!isSelected && existingIndex >= 0)
            {
                selectedItems.RemoveAt(existingIndex);
            }
        }
        catch (Exception exception) when (exception is NotSupportedException or ArgumentException or InvalidCastException)
        {
            option.IsSelected = ContainsSelectedItem(option.Item);
        }
        finally
        {
            _isSynchronizing = false;
            UpdateSelectionText();
        }
    }

    private IList EnsureSelectedItems()
    {
        if (SelectedItems is not null)
        {
            return SelectedItems;
        }

        var selectedItems = new ObservableCollection<object?>();
        _isSynchronizing = true;
        try
        {
            SetCurrentValue(SelectedItemsProperty, selectedItems);
        }
        finally
        {
            _isSynchronizing = false;
        }

        return selectedItems;
    }

    private bool ContainsSelectedItem(object? item)
    {
        return SelectedItems is not null && IndexOf(SelectedItems, item) >= 0;
    }

    private static int IndexOf(IList items, object? item)
    {
        for (var index = 0; index < items.Count; index++)
        {
            if (Equals(items[index], item))
            {
                return index;
            }
        }

        return -1;
    }

    private string ResolveDisplayText(object? item)
    {
        if (item is null)
        {
            return string.Empty;
        }

        if (string.IsNullOrWhiteSpace(DisplayMemberPath))
        {
            return item.ToString() ?? string.Empty;
        }

        var property = TypeDescriptor.GetProperties(item).Find(DisplayMemberPath, true);
        return property?.GetValue(item)?.ToString() ?? string.Empty;
    }

    private void UpdateSelectionText()
    {
        var ordinals = _options
            .Where(option => option.IsSelected)
            .Select(option => option.Ordinal)
            .ToArray();

        SetValue(
            SelectionTextPropertyKey,
            ordinals.Length == 0 ? Placeholder ?? string.Empty : string.Join(",", ordinals));
        UpdateClearOptionState();
    }

    private void UpdateClearOptionState()
    {
        ClearSelectionButton.IsEnabled = CanClearSelection(SelectedItems);
    }

    private static bool CanClearSelection(IList? selectedItems)
    {
        return selectedItems is { Count: > 0, IsReadOnly: false, IsFixedSize: false };
    }

    private sealed class DropDownCheckBoxOption : ObservableObject
    {
        private readonly DropDownCheckBox _owner;
        private bool _isSelected;

        public DropDownCheckBoxOption(
            DropDownCheckBox owner,
            object? item,
            int ordinal,
            string displayText,
            bool isSelected)
        {
            _owner = owner;
            Item = item;
            Ordinal = ordinal;
            DisplayText = displayText;
            _isSelected = isSelected;
        }

        public object? Item { get; }

        public int Ordinal { get; }

        public string DisplayText { get; }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (SetProperty(ref _isSelected, value))
                {
                    _owner.OnOptionSelectionChanged(this, value);
                }
            }
        }
    }
}
