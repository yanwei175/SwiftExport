using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace SwiftExportClient.Resources.Controls;

public enum LookupTextBoxSearchMode
{
    Auto,
    Local,
    Remote,
}

public partial class LookupTextBox : UserControl
{
    private readonly DispatcherTimer _debounceTimer;
    private readonly Dictionary<Type, Dictionary<string, PropertyInfo?>> _propertyCache = [];
    private bool _isInternalChanging;
    private bool _isSelectionDisplayMode;
    private bool _hasUserInputIntent;
    private INotifyCollectionChanged? _itemsCollectionChangedSource;

    public LookupTextBox()
    {
        InitializeComponent();

        _debounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _debounceTimer.Tick += DebounceTimer_Tick;

        Loaded += LookupTextBox_Loaded;
        Unloaded += LookupTextBox_Unloaded;
    }

    public static readonly DependencyProperty PlaceholderProperty =
        DependencyProperty.Register(
            nameof(Placeholder),
            typeof(string),
            typeof(LookupTextBox),
            new PropertyMetadata(string.Empty, OnPlaceholderChanged));

    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    public static readonly DependencyProperty SearchModeProperty =
        DependencyProperty.Register(
            nameof(SearchMode),
            typeof(LookupTextBoxSearchMode),
            typeof(LookupTextBox),
            new PropertyMetadata(LookupTextBoxSearchMode.Auto));

    public LookupTextBoxSearchMode SearchMode
    {
        get => (LookupTextBoxSearchMode)GetValue(SearchModeProperty);
        set => SetValue(SearchModeProperty, value);
    }

    public static readonly DependencyProperty SearchCommandProperty =
        DependencyProperty.Register(
            nameof(SearchCommand),
            typeof(ICommand),
            typeof(LookupTextBox),
            new PropertyMetadata(null));

    public ICommand? SearchCommand
    {
        get => (ICommand?)GetValue(SearchCommandProperty);
        set => SetValue(SearchCommandProperty, value);
    }

    public static readonly DependencyProperty QueryCommandProperty =
        DependencyProperty.Register(
            nameof(QueryCommand),
            typeof(ICommand),
            typeof(LookupTextBox),
            new PropertyMetadata(null));

    public ICommand? QueryCommand
    {
        get => (ICommand?)GetValue(QueryCommandProperty);
        set => SetValue(QueryCommandProperty, value);
    }

    public static readonly DependencyProperty SearchDelayProperty =
        DependencyProperty.Register(
            nameof(SearchDelay),
            typeof(TimeSpan),
            typeof(LookupTextBox),
            new PropertyMetadata(TimeSpan.FromMilliseconds(300), OnSearchDelayChanged),
            value => value is TimeSpan delay && delay >= TimeSpan.Zero);

    public TimeSpan SearchDelay
    {
        get => (TimeSpan)GetValue(SearchDelayProperty);
        set => SetValue(SearchDelayProperty, value);
    }

    public static readonly DependencyProperty MinimumSearchLengthProperty =
        DependencyProperty.Register(
            nameof(MinimumSearchLength),
            typeof(int),
            typeof(LookupTextBox),
            new PropertyMetadata(1, null, (_, value) => Math.Max(0, (int)value)));

    public int MinimumSearchLength
    {
        get => (int)GetValue(MinimumSearchLengthProperty);
        set => SetValue(MinimumSearchLengthProperty, value);
    }

    public static readonly DependencyProperty IsBusyProperty =
        DependencyProperty.Register(
            nameof(IsBusy),
            typeof(bool),
            typeof(LookupTextBox),
            new PropertyMetadata(false));

    public bool IsBusy
    {
        get => (bool)GetValue(IsBusyProperty);
        set => SetValue(IsBusyProperty, value);
    }

    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(
            nameof(Text),
            typeof(string),
            typeof(LookupTextBox),
            new FrameworkPropertyMetadata(
                string.Empty,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnTextChangedCallback));

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(
            nameof(ItemsSource),
            typeof(IEnumerable),
            typeof(LookupTextBox),
            new PropertyMetadata(null, OnItemsSourceChanged));

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public static readonly DependencyProperty DisplayMemberPathProperty =
        DependencyProperty.Register(
            nameof(DisplayMemberPath),
            typeof(string),
            typeof(LookupTextBox),
            new PropertyMetadata(string.Empty, OnDisplayMemberPathChanged));

    public string DisplayMemberPath
    {
        get => (string)GetValue(DisplayMemberPathProperty);
        set => SetValue(DisplayMemberPathProperty, value);
    }

    public static readonly DependencyProperty FilterMemberPathsProperty =
        DependencyProperty.Register(
            nameof(FilterMemberPaths),
            typeof(string),
            typeof(LookupTextBox),
            new PropertyMetadata(string.Empty));

    public string FilterMemberPaths
    {
        get => (string)GetValue(FilterMemberPathsProperty);
        set => SetValue(FilterMemberPathsProperty, value);
    }

    public static readonly DependencyProperty RequireItemMatchProperty =
        DependencyProperty.Register(
            nameof(RequireItemMatch),
            typeof(bool),
            typeof(LookupTextBox),
            new PropertyMetadata(false));

    public bool RequireItemMatch
    {
        get => (bool)GetValue(RequireItemMatchProperty);
        set => SetValue(RequireItemMatchProperty, value);
    }

    public static readonly DependencyProperty SelectedItemProperty =
        DependencyProperty.Register(
            nameof(SelectedItem),
            typeof(object),
            typeof(LookupTextBox),
            new FrameworkPropertyMetadata(
                null,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnSelectedItemChanged));

    public object? SelectedItem
    {
        get => GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    public static readonly DependencyProperty SelectionCommittedCommandProperty =
        DependencyProperty.Register(
            nameof(SelectionCommittedCommand),
            typeof(ICommand),
            typeof(LookupTextBox),
            new PropertyMetadata(null));

    public ICommand? SelectionCommittedCommand
    {
        get => (ICommand?)GetValue(SelectionCommittedCommandProperty);
        set => SetValue(SelectionCommittedCommandProperty, value);
    }

    public static readonly DependencyProperty SelectionCommittedCommandParameterProperty =
        DependencyProperty.Register(
            nameof(SelectionCommittedCommandParameter),
            typeof(object),
            typeof(LookupTextBox),
            new PropertyMetadata(null));

    public object? SelectionCommittedCommandParameter
    {
        get => GetValue(SelectionCommittedCommandParameterProperty);
        set => SetValue(SelectionCommittedCommandParameterProperty, value);
    }

    private static readonly DependencyPropertyKey CommitReasonPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(CommitReason),
            typeof(LookupCommitReason),
            typeof(LookupTextBox),
            new PropertyMetadata(LookupCommitReason.Programmatic));

    public static readonly DependencyProperty CommitReasonProperty = CommitReasonPropertyKey.DependencyProperty;

    public LookupCommitReason CommitReason
    {
        get => (LookupCommitReason)GetValue(CommitReasonProperty);
        private set => SetValue(CommitReasonPropertyKey, value);
    }

    public event EventHandler<LookupSelectionCommittedEventArgs>? SelectionCommitted;

    public static readonly DependencyProperty SelectedValuePathProperty =
        DependencyProperty.Register(
            nameof(SelectedValuePath),
            typeof(string),
            typeof(LookupTextBox),
            new PropertyMetadata(string.Empty));

    public string SelectedValuePath
    {
        get => (string)GetValue(SelectedValuePathProperty);
        set => SetValue(SelectedValuePathProperty, value);
    }

    public static readonly DependencyProperty SelectedValueProperty =
        DependencyProperty.Register(
            nameof(SelectedValue),
            typeof(object),
            typeof(LookupTextBox),
            new FrameworkPropertyMetadata(
                null,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public object? SelectedValue
    {
        get => GetValue(SelectedValueProperty);
        set => SetValue(SelectedValueProperty, value);
    }

    public static readonly DependencyProperty IsDropDownOpenProperty =
        DependencyProperty.Register(
            nameof(IsDropDownOpen),
            typeof(bool),
            typeof(LookupTextBox),
            new FrameworkPropertyMetadata(
                false,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public bool IsDropDownOpen
    {
        get => (bool)GetValue(IsDropDownOpenProperty);
        set => SetValue(IsDropDownOpenProperty, value);
    }

    public static readonly DependencyProperty ItemTemplateProperty =
        DependencyProperty.Register(
            nameof(ItemTemplate),
            typeof(DataTemplate),
            typeof(LookupTextBox),
            new PropertyMetadata(null, OnItemTemplateChanged));

    public DataTemplate? ItemTemplate
    {
        get => (DataTemplate?)GetValue(ItemTemplateProperty);
        set => SetValue(ItemTemplateProperty, value);
    }

    public static readonly DependencyProperty HasTextOrSelectionProperty =
        DependencyProperty.Register(
            nameof(HasTextOrSelection),
            typeof(bool),
            typeof(LookupTextBox),
            new PropertyMetadata(false));

    public bool HasTextOrSelection
    {
        get => (bool)GetValue(HasTextOrSelectionProperty);
        private set => SetValue(HasTextOrSelectionProperty, value);
    }

    public ICollectionView? FilteredView { get; private set; }

    public void FocusEditor()
    {
        Dispatcher.BeginInvoke(
            new Action(() =>
            {
                InputTextBox.Focus();
                Keyboard.Focus(InputTextBox);
                InputTextBox.SelectAll();
            }),
            DispatcherPriority.Input);
    }

    private static void OnPlaceholderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((LookupTextBox)d).UpdateTextVisualState();
    }

    private static void OnSearchDelayChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((LookupTextBox)d)._debounceTimer.Interval = (TimeSpan)e.NewValue;
    }

    private static void OnTextChangedCallback(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (LookupTextBox)d;
        if (control._isInternalChanging)
        {
            return;
        }

        control._isSelectionDisplayMode = false;
        control.SetInputTextSilently(e.NewValue?.ToString() ?? string.Empty);
        control.UpdateTextVisualState();
    }

    private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (LookupTextBox)d;
        control.DetachCollectionChanged();

        if (e.NewValue is IEnumerable source)
        {
            var cvs = new CollectionViewSource { Source = source };
            control.FilteredView = cvs.View;
            control.FilteredView.Filter = control.FilterItem;
            control.ResultList.ItemsSource = control.FilteredView;
            control.ApplyItemTemplate();
            control.AttachCollectionChanged(source);
            control.UpdateDropDownAfterItemsChanged();
        }
        else
        {
            control.FilteredView = null;
            control.ResultList.ItemsSource = null;
            control.SetSelectedItemSilently(null);
            control.SelectedValue = null;
            control.SetDisplayText(string.Empty);
            control.IsDropDownOpen = false;
        }

        control.UpdateTextVisualState();
    }

    private static void OnDisplayMemberPathChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (LookupTextBox)d;
        control.ApplyItemTemplate();
        control.UpdateDisplayTextFromSelectedItem();
    }

    private static void OnSelectedItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (LookupTextBox)d;
        if (control._isInternalChanging)
        {
            return;
        }

        if (e.NewValue is null)
        {
            control.SelectedValue = null;
            control.SetDisplayText(string.Empty);
            control._isSelectionDisplayMode = false;
            control.UpdateTextVisualState();
            return;
        }

        control.SelectedValue = control.GetSelectedValue(e.NewValue);
        control.SetDisplayText(control.GetDisplayText(e.NewValue));
        control.ClearInputTextSilently();
        control._isSelectionDisplayMode = true;
        control.UpdateTextVisualState();
    }

    private static void OnItemTemplateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((LookupTextBox)d).ApplyItemTemplate();
    }

    private void LookupTextBox_Loaded(object sender, RoutedEventArgs e)
    {
        _hasUserInputIntent = false;
        ApplyItemTemplate();
        UpdateDisplayTextFromSelectedItem();
        UpdateTextVisualState();
        if (IsKeyboardFocusWithin || IsFocused)
        {
            FocusEditor();
        }
    }

    private void LookupTextBox_Unloaded(object sender, RoutedEventArgs e)
    {
        _debounceTimer.Stop();
        _hasUserInputIntent = false;
        IsDropDownOpen = false;
        DetachCollectionChanged();
    }

    private void InputTextBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        _hasUserInputIntent = false;
        UpdateTextVisualState();
    }

    private void InputTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isInternalChanging)
        {
            return;
        }

        var newText = InputTextBox.Text ?? string.Empty;
        _isSelectionDisplayMode = false;
        _hasUserInputIntent = IsLoaded && InputTextBox.IsKeyboardFocusWithin;

        if (SelectedItem is not null)
        {
            SetSelectedItemSilently(null);
            SelectedValue = null;
            SetDisplayText(string.Empty);
        }

        SyncTextDependencySilently(newText);
        UpdateTextVisualState();

        if (!_hasUserInputIntent)
        {
            _debounceTimer.Stop();
            return;
        }

        if (string.IsNullOrWhiteSpace(newText))
        {
            _debounceTimer.Stop();
            FilteredView?.Refresh();
            IsDropDownOpen = false;
            return;
        }

        if (ShouldUseLocalFilter())
        {
            _debounceTimer.Stop();
            _debounceTimer.Start();
        }
        else if (SearchMode == LookupTextBoxSearchMode.Remote && SearchCommand is not null)
        {
            _debounceTimer.Stop();
            if (IsLoaded && InputTextBox.IsKeyboardFocusWithin)
            {
                _debounceTimer.Start();
            }
        }
        else
        {
            _debounceTimer.Stop();
            OpenDropDownFromUserIntent(selectFirstItem: false);
        }
    }

    private void DebounceTimer_Tick(object? sender, EventArgs e)
    {
        _debounceTimer.Stop();

        if (!HasActiveUserQuery())
        {
            IsDropDownOpen = false;
            return;
        }

        if (!ShouldUseLocalFilter())
        {
            ExecuteSearchCommand();
            return;
        }

        ResolveSelectionByText();
        RefreshAndUpdateDropDown();
    }

    private void BtnClear_Click(object sender, RoutedEventArgs e)
    {
        _debounceTimer.Stop();
        _hasUserInputIntent = false;
        _isSelectionDisplayMode = false;
        SetSelectedItemSilently(null);
        SelectedValue = null;
        SetDisplayText(string.Empty);
        SetInputTextSilently(string.Empty);
        SyncTextDependencySilently(string.Empty);
        FilteredView?.Refresh();
        IsDropDownOpen = false;
        UpdateTextVisualState();
        InputTextBox.Focus();
    }

    private void InputTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space && string.IsNullOrWhiteSpace(InputTextBox.Text))
        {
            OpenDropDownFromUserIntent(selectFirstItem: true);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Down && !IsDropDownOpen)
        {
            OpenDropDownFromUserIntent(selectFirstItem: true);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter && (!IsDropDownOpen || ResultList.Items.Count == 0))
        {
            ExecuteQueryCommand();
            e.Handled = true;
            return;
        }

        if (!IsDropDownOpen || ResultList.Items.Count == 0)
        {
            return;
        }

        if (e.Key == Key.Down)
        {
            ResultList.SelectedIndex = Math.Min(ResultList.SelectedIndex + 1, ResultList.Items.Count - 1);
            ResultList.ScrollIntoView(ResultList.SelectedItem);
            e.Handled = true;
        }
        else if (e.Key == Key.Up)
        {
            ResultList.SelectedIndex = Math.Max(ResultList.SelectedIndex - 1, 0);
            ResultList.ScrollIntoView(ResultList.SelectedItem);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter || e.Key == Key.Tab)
        {
            if (ResultList.SelectedItem is not null)
            {
                CommitSelection(ResultList.SelectedItem, e.Key == Key.Tab ? LookupCommitReason.Tab : LookupCommitReason.Enter);
            }
            else if (e.Key == Key.Enter)
            {
                ExecuteQueryCommand();
            }
            e.Handled = e.Key != Key.Tab;
        }
        else if (e.Key == Key.Escape)
        {
            IsDropDownOpen = false;
            e.Handled = true;
        }
    }

    private void InputTextBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        var wasDropDownOpen = IsDropDownOpen;
        _debounceTimer.Stop();
        _hasUserInputIntent = false;
        IsDropDownOpen = false;

        if (!RequireItemMatch || wasDropDownOpen)
        {
            UpdateTextVisualState();
            return;
        }

        var currentText = (InputTextBox.Text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(currentText))
        {
            UpdateTextVisualState();
            return;
        }

        var hasMatch = ResolveSelectionByText();
        if (!hasMatch)
        {
            SetInputTextSilently(string.Empty);
            SyncTextDependencySilently(string.Empty);
            SetSelectedItemSilently(null);
            SelectedValue = null;
            SetDisplayText(string.Empty);
        }

        UpdateTextVisualState();
    }

    private void ResultList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject originalSource)
        {
            return;
        }

        var clickedItem = FindAncestor<ListBoxItem>(originalSource);
        if (clickedItem is null)
        {
            return;
        }

        CommitSelection(clickedItem.DataContext, LookupCommitReason.Mouse);
        e.Handled = true;
    }

    private void EditorBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_isSelectionDisplayMode)
        {
            BeginEditSelection();
            e.Handled = true;
            return;
        }

        InputTextBox.Focus();
    }

    private void ItemsSource_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(() => ItemsSource_CollectionChanged(sender, e)));
            return;
        }

        UpdateDropDownAfterItemsChanged();
    }

    private bool FilterItem(object item)
    {
        if (!ShouldUseLocalFilter())
        {
            return true;
        }

        var input = InputTextBox.Text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(input))
        {
            return true;
        }

        foreach (var path in ParseFilterMemberPaths())
        {
            var value = GetMemberText(item, path);
            if (!string.IsNullOrEmpty(value) &&
                value.Contains(input, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private void CommitSelection(object? item, LookupCommitReason reason)
    {
        if (item is null)
        {
            return;
        }

        _debounceTimer.Stop();
        _hasUserInputIntent = false;
        SetSelectedItemSilently(item);
        SelectedValue = GetSelectedValue(item);
        SetDisplayText(GetDisplayText(item));
        ClearInputTextSilently();
        _isSelectionDisplayMode = true;
        IsDropDownOpen = false;
        CommitReason = reason;
        UpdateTextVisualState();
        ExecuteSelectionCommittedCommand(item, reason);
        SelectionCommitted?.Invoke(
            this,
            new LookupSelectionCommittedEventArgs(item, SelectedValue, Text, reason));
    }

    private bool ResolveSelectionByText()
    {
        var text = (InputTextBox.Text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text) || ItemsSource is null)
        {
            return string.IsNullOrWhiteSpace(text);
        }

        foreach (var item in ItemsSource)
        {
            if (item is null)
            {
                continue;
            }

            foreach (var path in ParseResolveMemberPaths())
            {
                var value = GetMemberText(item, path);
                if (string.Equals((value ?? string.Empty).Trim(), text, StringComparison.OrdinalIgnoreCase))
                {
                    SetSelectedItemSilently(item);
                    SelectedValue = GetSelectedValue(item);
                    SetDisplayText(GetDisplayText(item));
                    CommitReason = LookupCommitReason.ExactMatch;
                    return true;
                }
            }
        }

        SetSelectedItemSilently(null);
        SelectedValue = null;
        SetDisplayText(string.Empty);
        return false;
    }

    private void BeginEditSelection()
    {
        _hasUserInputIntent = false;
        _isSelectionDisplayMode = false;
        SetSelectedItemSilently(null);
        SelectedValue = null;
        SetDisplayText(string.Empty);
        SetInputTextSilently(string.Empty);
        SyncTextDependencySilently(string.Empty);
        IsDropDownOpen = false;
        UpdateTextVisualState();
        InputTextBox.Focus();
    }

    private void ExecuteSelectionCommittedCommand(object item, LookupCommitReason reason)
    {
        var command = SelectionCommittedCommand;
        var parameter = SelectionCommittedCommandParameter ?? item;

        if (command?.CanExecute(parameter) == true)
        {
            command.Execute(parameter);
        }
    }

    private void ExecuteSearchCommand()
    {
        if (SearchMode == LookupTextBoxSearchMode.Remote &&
            (!IsLoaded || !InputTextBox.IsKeyboardFocusWithin))
        {
            return;
        }

        var text = (InputTextBox.Text ?? string.Empty).Trim();
        if (text.Length < MinimumSearchLength)
        {
            IsDropDownOpen = false;
            return;
        }

        if (SearchCommand?.CanExecute(text) == true)
        {
            SearchCommand.Execute(text);
        }
    }

    private void ExecuteQueryCommand()
    {
        var text = (InputTextBox.Text ?? string.Empty).Trim();
        if (QueryCommand?.CanExecute(text) == true)
        {
            QueryCommand.Execute(text);
        }
    }

    private void ApplyItemTemplate()
    {
        if (ItemTemplate is not null)
        {
            ResultList.DisplayMemberPath = string.Empty;
            ResultList.ItemTemplate = ItemTemplate;
        }
        else
        {
            ResultList.ItemTemplate = null;
            ResultList.DisplayMemberPath = DisplayMemberPath ?? string.Empty;
        }
    }

    private void RefreshAndUpdateDropDown()
    {
        if (FilteredView is null)
        {
            IsDropDownOpen = false;
            return;
        }

        FilteredView.Refresh();
        IsDropDownOpen = !FilteredView.IsEmpty;
    }

    private bool OpenDropDownFromUserIntent(bool selectFirstItem)
    {
        if (FilteredView is null)
        {
            IsDropDownOpen = false;
            return false;
        }

        FilteredView.Refresh();
        if (FilteredView.IsEmpty)
        {
            IsDropDownOpen = false;
            return false;
        }

        if (selectFirstItem)
        {
            ResultList.SelectedIndex = 0;
            ResultList.ScrollIntoView(ResultList.SelectedItem);
        }

        IsDropDownOpen = true;
        return true;
    }

    private void UpdateDropDownAfterItemsChanged()
    {
        if (FilteredView is null)
        {
            IsDropDownOpen = false;
            return;
        }

        FilteredView.Refresh();
        if (IsDropDownOpen || HasActiveUserQuery())
        {
            IsDropDownOpen = !FilteredView.IsEmpty;
        }
    }

    private bool HasActiveUserQuery()
    {
        if (!_hasUserInputIntent || !IsLoaded || !InputTextBox.IsKeyboardFocusWithin)
        {
            return false;
        }

        var input = (InputTextBox.Text ?? string.Empty).Trim();
        return input.Length >= Math.Max(1, MinimumSearchLength);
    }

    private void UpdateDisplayTextFromSelectedItem()
    {
        SetDisplayText(SelectedItem is null ? string.Empty : GetDisplayText(SelectedItem));
    }

    private void SetDisplayText(string? text)
    {
        DisplayTextBlock.Text = text ?? string.Empty;
        UpdateTextVisualState();
    }

    private void UpdateTextVisualState()
    {
        var hasInput = !string.IsNullOrWhiteSpace(InputTextBox.Text);
        var hasDisplay = !string.IsNullOrWhiteSpace(DisplayTextBlock.Text);

        if (_isSelectionDisplayMode && hasDisplay)
        {
            InputTextBox.Visibility = Visibility.Collapsed;
            DisplayTextBlock.Visibility = Visibility.Visible;
            PlaceholderTextBlock.Visibility = Visibility.Collapsed;
            HasTextOrSelection = true;
            return;
        }

        InputTextBox.Visibility = Visibility.Visible;
        DisplayTextBlock.Visibility = Visibility.Collapsed;
        PlaceholderTextBlock.Visibility = hasInput || hasDisplay ? Visibility.Collapsed : Visibility.Visible;
        HasTextOrSelection = hasInput || hasDisplay;
    }

    private bool ShouldUseLocalFilter()
    {
        if (SearchMode == LookupTextBoxSearchMode.Local)
        {
            return true;
        }

        if (SearchMode == LookupTextBoxSearchMode.Remote)
        {
            return false;
        }

        var bindingExpression = BindingOperations.GetBindingExpression(this, TextProperty);
        return bindingExpression is null;
    }

    private string GetDisplayText(object item)
    {
        if (!string.IsNullOrWhiteSpace(DisplayMemberPath))
        {
            var prop = GetCachedProperty(item.GetType(), DisplayMemberPath);
            return prop?.GetValue(item)?.ToString() ?? string.Empty;
        }

        return item.ToString() ?? string.Empty;
    }

    private object? GetSelectedValue(object item)
    {
        if (string.IsNullOrWhiteSpace(SelectedValuePath))
        {
            return item;
        }

        var prop = GetCachedProperty(item.GetType(), SelectedValuePath);
        return prop?.GetValue(item);
    }

    private string GetMemberText(object item, string memberPath)
    {
        if (string.IsNullOrWhiteSpace(memberPath))
        {
            return item.ToString() ?? string.Empty;
        }

        var prop = GetCachedProperty(item.GetType(), memberPath);
        return prop?.GetValue(item)?.ToString() ?? string.Empty;
    }

    private PropertyInfo? GetCachedProperty(Type type, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(propertyName))
        {
            return null;
        }

        if (!_propertyCache.TryGetValue(type, out var typeProps))
        {
            typeProps = new Dictionary<string, PropertyInfo?>(StringComparer.OrdinalIgnoreCase);
            _propertyCache[type] = typeProps;
        }

        if (!typeProps.TryGetValue(propertyName, out var propInfo))
        {
            propInfo = type.GetProperty(
                propertyName,
                BindingFlags.IgnoreCase | BindingFlags.Public | BindingFlags.Instance);

            typeProps[propertyName] = propInfo;
        }

        return propInfo;
    }

    private List<string> ParseFilterMemberPaths()
    {
        if (!string.IsNullOrWhiteSpace(FilterMemberPaths))
        {
            var paths = FilterMemberPaths
                .Split([',', ';', '|'], StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (paths.Count > 0)
            {
                return paths;
            }
        }

        return !string.IsNullOrWhiteSpace(DisplayMemberPath)
            ? [DisplayMemberPath]
            : [string.Empty];
    }

    private List<string> ParseResolveMemberPaths()
    {
        var paths = ParseFilterMemberPaths();

        if (!string.IsNullOrWhiteSpace(DisplayMemberPath) &&
            !paths.Contains(DisplayMemberPath, StringComparer.OrdinalIgnoreCase))
        {
            paths.Insert(0, DisplayMemberPath);
        }

        return paths;
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
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

    private void SetSelectedItemSilently(object? item)
    {
        if (Equals(SelectedItem, item))
        {
            return;
        }

        _isInternalChanging = true;
        try
        {
            SelectedItem = item;
        }
        finally
        {
            _isInternalChanging = false;
        }
    }

    private void SetInputTextSilently(string? text)
    {
        var target = text ?? string.Empty;
        var current = InputTextBox.Text ?? string.Empty;

        if (string.Equals(current, target, StringComparison.Ordinal))
        {
            return;
        }

        _isInternalChanging = true;
        try
        {
            InputTextBox.Text = target;
            InputTextBox.CaretIndex = target.Length;
        }
        finally
        {
            _isInternalChanging = false;
        }
    }

    private void ClearInputTextSilently()
    {
        SetInputTextSilently(string.Empty);
    }

    private void SyncTextDependencySilently(string? text)
    {
        var target = text ?? string.Empty;
        var current = Text ?? string.Empty;

        if (string.Equals(current, target, StringComparison.Ordinal))
        {
            return;
        }

        _isInternalChanging = true;
        try
        {
            Text = target;
        }
        finally
        {
            _isInternalChanging = false;
        }
    }

    private void AttachCollectionChanged(IEnumerable source)
    {
        _itemsCollectionChangedSource = source as INotifyCollectionChanged;
        if (_itemsCollectionChangedSource is not null)
        {
            _itemsCollectionChangedSource.CollectionChanged += ItemsSource_CollectionChanged;
        }
    }

    private void DetachCollectionChanged()
    {
        if (_itemsCollectionChangedSource is not null)
        {
            _itemsCollectionChangedSource.CollectionChanged -= ItemsSource_CollectionChanged;
            _itemsCollectionChangedSource = null;
        }
    }
}
