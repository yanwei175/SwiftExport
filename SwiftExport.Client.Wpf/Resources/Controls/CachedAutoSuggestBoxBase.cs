using System.Collections;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Wpf.Ui.Controls;

namespace SwiftExportClient.Resources.Controls;

/// <summary>
/// 为缓存型业务 AutoSuggestBox 提供本地筛选、选中回填和 MVVM 命令适配。
/// </summary>
public class CachedAutoSuggestBoxBase : UserControl
{
    private List<object> _sourceItems = [];
    private CommittedAutoSuggestBox? _editor;
    private INotifyCollectionChanged? _observedCollection;
    private object? _committedItem;
    private bool _isSynchronizing;

    /// <summary>
    /// 标识外部完整缓存集合。
    /// </summary>
    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource),
        typeof(IList),
        typeof(CachedAutoSuggestBoxBase),
        new PropertyMetadata(null, OnItemsSourceChanged));

    /// <summary>
    /// 标识用户当前搜索文字；选中对象的显示回填不会写入此属性。
    /// </summary>
    public static readonly DependencyProperty SearchTextProperty = DependencyProperty.Register(
        nameof(SearchText),
        typeof(string),
        typeof(CachedAutoSuggestBoxBase),
        new FrameworkPropertyMetadata(
            string.Empty,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            OnSearchTextChanged));

    /// <summary>
    /// 标识选择确认命令。
    /// </summary>
    public static readonly DependencyProperty SelectionCommittedCommandProperty = Register(
        nameof(SelectionCommittedCommand), typeof(ICommand));

    /// <summary>
    /// 标识选择确认命令的业务上下文参数。
    /// </summary>
    public static readonly DependencyProperty SelectionCommittedCommandParameterProperty = Register(
        nameof(SelectionCommittedCommandParameter), typeof(object));

    /// <summary>
    /// 标识用户清空已提交选择时执行的命令。
    /// </summary>
    public static readonly DependencyProperty SelectionClearedCommandProperty = Register(
        nameof(SelectionClearedCommand), typeof(ICommand));

    /// <summary>
    /// 标识清空命令的业务上下文参数。
    /// </summary>
    public static readonly DependencyProperty SelectionClearedCommandParameterProperty = Register(
        nameof(SelectionClearedCommandParameter), typeof(object));

    /// <summary>
    /// 标识输入框提示文字。
    /// </summary>
    public static readonly DependencyProperty PlaceholderTextProperty = Register(
        nameof(PlaceholderText), typeof(string), string.Empty);

    /// <summary>
    /// 标识候选弹层是否打开。
    /// </summary>
    public static readonly DependencyProperty IsSuggestionListOpenProperty = DependencyProperty.Register(
        nameof(IsSuggestionListOpen),
        typeof(bool),
        typeof(CachedAutoSuggestBoxBase),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    /// <summary>
    /// 标识候选弹层最大高度。
    /// </summary>
    public static readonly DependencyProperty MaxSuggestionListHeightProperty = Register(
        nameof(MaxSuggestionListHeight), typeof(double), 240d);

    /// <summary>
    /// 标识输入框是否显示清空按钮。
    /// </summary>
    public static readonly DependencyProperty ClearButtonEnabledProperty = Register(
        nameof(ClearButtonEnabled), typeof(bool), true);

    /// <summary>
    /// 初始化缓存搜索控件的生命周期订阅。
    /// </summary>
    public CachedAutoSuggestBoxBase()
    {
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    /// <summary>
    /// 获取或设置供当前控件读取的完整缓存集合。
    /// </summary>
    public IList? ItemsSource
    {
        get => (IList?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    /// <summary>
    /// 获取或设置用户当前输入的搜索文字。
    /// </summary>
    public string SearchText
    {
        get => (string)GetValue(SearchTextProperty);
        set => SetValue(SearchTextProperty, value);
    }

    /// <summary>
    /// 获取或设置用户确认选择后执行的命令；未提供参数时传递完整 DTO。
    /// </summary>
    public ICommand? SelectionCommittedCommand
    {
        get => (ICommand?)GetValue(SelectionCommittedCommandProperty);
        set => SetValue(SelectionCommittedCommandProperty, value);
    }

    /// <summary>
    /// 获取或设置选择确认命令的业务上下文参数。
    /// </summary>
    public object? SelectionCommittedCommandParameter
    {
        get => GetValue(SelectionCommittedCommandParameterProperty);
        set => SetValue(SelectionCommittedCommandParameterProperty, value);
    }

    /// <summary>
    /// 获取或设置用户清空已提交选择后执行的命令。
    /// </summary>
    public ICommand? SelectionClearedCommand
    {
        get => (ICommand?)GetValue(SelectionClearedCommandProperty);
        set => SetValue(SelectionClearedCommandProperty, value);
    }

    /// <summary>
    /// 获取或设置清空命令的业务上下文参数。
    /// </summary>
    public object? SelectionClearedCommandParameter
    {
        get => GetValue(SelectionClearedCommandParameterProperty);
        set => SetValue(SelectionClearedCommandParameterProperty, value);
    }

    /// <summary>
    /// 获取或设置输入框提示文字。
    /// </summary>
    public string PlaceholderText
    {
        get => (string)GetValue(PlaceholderTextProperty);
        set => SetValue(PlaceholderTextProperty, value);
    }

    /// <summary>
    /// 获取或设置候选弹层状态。
    /// </summary>
    public bool IsSuggestionListOpen
    {
        get => (bool)GetValue(IsSuggestionListOpenProperty);
        set => SetValue(IsSuggestionListOpenProperty, value);
    }

    /// <summary>
    /// 获取或设置候选弹层最大高度。
    /// </summary>
    public double MaxSuggestionListHeight
    {
        get => (double)GetValue(MaxSuggestionListHeightProperty);
        set => SetValue(MaxSuggestionListHeightProperty, value);
    }

    /// <summary>
    /// 获取或设置是否显示 WPF-UI 清空按钮。
    /// </summary>
    public bool ClearButtonEnabled
    {
        get => (bool)GetValue(ClearButtonEnabledProperty);
        set => SetValue(ClearButtonEnabledProperty, value);
    }

    /// <summary>
    /// 将键盘焦点移入内部输入框。
    /// </summary>
    public void FocusEditor() => _editor?.Focus();

    /// <summary>
    /// 由具体业务控件连接内部编辑器；只应在 InitializeComponent 完成后调用一次。
    /// </summary>
    /// <param name="editor">承载 WPF-UI 交互的编辑器。</param>
    protected void InitializeEditor(CommittedAutoSuggestBox editor)
    {
        _editor = editor;
        editor.SuggestionChosen += EditorOnSuggestionChosen;
        editor.TextChanged += EditorOnTextChanged;
        editor.EditAbandoned += EditorOnEditAbandoned;
        editor.UserClearRequested += EditorOnUserClearRequested;
        RebuildSourceItems();
        RestoreCommittedDisplay();
    }

    /// <summary>
    /// 当外部设置强类型选中对象时同步显示，但不触发提交或搜索。
    /// </summary>
    /// <param name="item">新的完整 DTO。</param>
    protected void SynchronizeSelectedItem(object? item)
    {
        if (_isSynchronizing || ReferenceEquals(_committedItem, item))
        {
            return;
        }

        _committedItem = item;
        RestoreCommittedDisplay();
    }

    /// <summary>
    /// 返回具体业务控件当前已提交的强类型对象。
    /// </summary>
    protected virtual object? GetSelectedItemCore() => null;

    /// <summary>
    /// 更新具体业务控件的强类型选中对象，并保留外部 Binding。
    /// </summary>
    /// <param name="item">待提交或清除的完整 DTO。</param>
    protected virtual void SetSelectedItemCore(object? item)
    {
    }

    /// <summary>
    /// 返回单字段搜索及选中显示文字。
    /// </summary>
    /// <param name="item">完整 DTO。</param>
    protected virtual string GetDisplayText(object item) => item.ToString() ?? string.Empty;

    private static DependencyProperty Register(string name, Type type, object? defaultValue = null) =>
        DependencyProperty.Register(name, type, typeof(CachedAutoSuggestBoxBase), new PropertyMetadata(defaultValue));

    private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (CachedAutoSuggestBoxBase)d;
        control.ReplaceCollectionSubscription(e.OldValue as INotifyCollectionChanged, e.NewValue as INotifyCollectionChanged);
        control.RebuildSourceItems();
    }

    private static void OnSearchTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (CachedAutoSuggestBoxBase)d;
        if (control._isSynchronizing || control._editor is null)
        {
            return;
        }

        var text = e.NewValue as string ?? string.Empty;
        control.SetEditorText(text);
        control.ApplyFilter(text, control.IsSuggestionListOpen && !string.IsNullOrEmpty(text));
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ReplaceCollectionSubscription(null, ItemsSource as INotifyCollectionChanged);
        RebuildSourceItems();
        SynchronizeSelectedItem(GetSelectedItemCore());
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        ReplaceCollectionSubscription(_observedCollection, null);
        SetCurrentValue(IsSuggestionListOpenProperty, false);
    }

    private void ReplaceCollectionSubscription(
        INotifyCollectionChanged? oldCollection,
        INotifyCollectionChanged? newCollection)
    {
        if (oldCollection is not null)
        {
            oldCollection.CollectionChanged -= ItemsSourceOnCollectionChanged;
        }

        _observedCollection = IsLoaded ? newCollection : null;
        if (_observedCollection is not null)
        {
            _observedCollection.CollectionChanged -= ItemsSourceOnCollectionChanged;
            _observedCollection.CollectionChanged += ItemsSourceOnCollectionChanged;
        }
    }

    private void ItemsSourceOnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RebuildSourceItems();
    }

    private void RebuildSourceItems()
    {
        _sourceItems = ItemsSource?.Cast<object>().Where(static item => item is not null).ToList() ?? [];

        ApplyFilter(SearchText, IsSuggestionListOpen);
    }

    private void ApplyFilter(string text, bool keepOpen)
    {
        if (_editor is null)
        {
            return;
        }

        if (string.IsNullOrEmpty(text))
        {
            _editor.SetCurrentValue(ItemsControl.ItemsSourceProperty, _sourceItems);
        }
        else
        {
            var keys = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            _editor.SetCurrentValue(
                ItemsControl.ItemsSourceProperty,
                _sourceItems
                    .Where(item => keys.All(key =>
                        GetDisplayText(item).Contains(key, StringComparison.OrdinalIgnoreCase)))
                    .ToList());
        }

        _editor.SetCurrentValue(AutoSuggestBox.IsSuggestionListOpenProperty, keepOpen);
    }

    private void EditorOnSuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        if (args.SelectedItem is not { } selectedItem)
        {
            return;
        }

        _isSynchronizing = true;
        try
        {
            SetSelectedItemCore(selectedItem);
            _committedItem = selectedItem;
            SetCurrentValue(SearchTextProperty, string.Empty);
        }
        finally
        {
            _isSynchronizing = false;
        }

        ApplyFilter(string.Empty, false);
        SetEditorText(GetDisplayText(selectedItem));
        ExecuteCommand(SelectionCommittedCommand, SelectionCommittedCommandParameter ?? selectedItem);
    }

    private void EditorOnTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (_isSynchronizing || args.Reason is not AutoSuggestionBoxTextChangeReason.UserInput)
        {
            return;
        }

        args.Handled = true;
        SetSearchTextFromControl(args.Text);
        ApplyFilter(args.Text, IsSuggestionListOpen);
    }

    private void EditorOnEditAbandoned(object? sender, EventArgs e) => RestoreCommittedDisplay();

    private void EditorOnUserClearRequested(object? sender, EventArgs e)
    {
        SetSearchTextFromControl(string.Empty);
        ApplyFilter(string.Empty, false);
        ClearCommittedSelectionFromUser();
        _editor?.Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            new Action(() => _editor?.SetCurrentValue(AutoSuggestBox.IsSuggestionListOpenProperty, false)));
    }

    private void ClearCommittedSelectionFromUser()
    {
        if (GetSelectedItemCore() is null)
        {
            return;
        }

        _isSynchronizing = true;
        try
        {
            SetSelectedItemCore(null);
            _committedItem = null;
        }
        finally
        {
            _isSynchronizing = false;
        }

        ExecuteCommand(SelectionClearedCommand, SelectionClearedCommandParameter);
    }

    private void RestoreCommittedDisplay()
    {
        SetSearchTextFromControl(string.Empty);
        ApplyFilter(string.Empty, false);
        SetEditorText(_committedItem is null ? string.Empty : GetDisplayText(_committedItem));
        _editor?.SetCurrentValue(AutoSuggestBox.IsSuggestionListOpenProperty, false);
    }

    private void SetSearchTextFromControl(string text)
    {
        if (string.Equals(SearchText, text, StringComparison.Ordinal))
        {
            return;
        }

        _isSynchronizing = true;
        try
        {
            SetCurrentValue(SearchTextProperty, text);
        }
        finally
        {
            _isSynchronizing = false;
        }
    }

    private void SetEditorText(string text)
    {
        if (_editor is null || string.Equals(_editor.Text, text, StringComparison.Ordinal))
        {
            return;
        }

        _isSynchronizing = true;
        try
        {
            _editor.SetCurrentValue(AutoSuggestBox.TextProperty, text);
        }
        finally
        {
            _isSynchronizing = false;
        }
    }

    private static void ExecuteCommand(ICommand? command, object? parameter)
    {
        if (command?.CanExecute(parameter) is true)
        {
            command.Execute(parameter);
        }
    }
}
