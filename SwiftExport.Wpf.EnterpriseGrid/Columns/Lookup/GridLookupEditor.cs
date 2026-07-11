using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using SwiftExport.Wpf.EnterpriseGrid.Columns.Lookup.Internal;

namespace SwiftExport.Wpf.EnterpriseGrid.Columns.Lookup;

/// <summary>
/// 原生 ComboBox Lookup 编辑器；Provider 模式支持去抖、取消、空结果和已保存值回填。
/// </summary>
public class GridLookupEditor : ComboBox
{
    /// <summary>
    /// 与业务行双向绑定的 Lookup 值。它独立于原生 SelectedValue，候选尚未加载时也不会丢失。
    /// </summary>
    public static readonly DependencyProperty LookupValueProperty = DependencyProperty.Register(
        nameof(LookupValue),
        typeof(object),
        typeof(GridLookupEditor),
        new FrameworkPropertyMetadata(
            null,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            OnLookupValueChanged));

    /// <summary>远程 Lookup 提供器；为空时作为普通本地 ComboBox 使用。</summary>
    public static readonly DependencyProperty ProviderProperty = DependencyProperty.Register(
        nameof(Provider),
        typeof(IGridLookupProvider),
        typeof(GridLookupEditor),
        new PropertyMetadata(null, OnProviderChanged));

    /// <summary>搜索去抖时长。</summary>
    public static readonly DependencyProperty SearchDelayProperty = DependencyProperty.Register(
        nameof(SearchDelay),
        typeof(TimeSpan),
        typeof(GridLookupEditor),
        new PropertyMetadata(TimeSpan.FromMilliseconds(300)),
        value => (TimeSpan)value >= TimeSpan.Zero);

    /// <summary>触发远程搜索的最少字符数。</summary>
    public static readonly DependencyProperty MinimumSearchLengthProperty = DependencyProperty.Register(
        nameof(MinimumSearchLength),
        typeof(int),
        typeof(GridLookupEditor),
        new PropertyMetadata(1),
        value => (int)value >= 0);

    private static readonly DependencyPropertyKey IsSearchingPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(IsSearching),
        typeof(bool),
        typeof(GridLookupEditor),
        new PropertyMetadata(false));

    /// <summary>只读搜索中状态。</summary>
    public static readonly DependencyProperty IsSearchingProperty = IsSearchingPropertyKey.DependencyProperty;

    private static readonly DependencyPropertyKey SearchErrorPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(SearchError),
        typeof(string),
        typeof(GridLookupEditor),
        new PropertyMetadata(null));

    /// <summary>只读搜索错误文本；Provider 异常不会弹窗或越过 Grid 边界。</summary>
    public static readonly DependencyProperty SearchErrorProperty = SearchErrorPropertyKey.DependencyProperty;

    private CancellationTokenSource? _searchCancellation;
    private CancellationTokenSource? _resolveCancellation;
    private TextBox? _editableTextBox;
    private bool _isLoaded;

    /// <summary>初始化可编辑的原生 ComboBox Lookup 编辑器。</summary>
    public GridLookupEditor()
    {
        IsEditable = true;
        IsTextSearchEnabled = true;
        StaysOpenOnEdit = true;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    /// <summary>获取或设置远程 Lookup 提供器。</summary>
    public IGridLookupProvider? Provider
    {
        get => (IGridLookupProvider?)GetValue(ProviderProperty);
        set => SetValue(ProviderProperty, value);
    }

    /// <summary>获取或设置与业务行绑定的稳定 Lookup 值。</summary>
    public object? LookupValue
    {
        get => GetValue(LookupValueProperty);
        set => SetValue(LookupValueProperty, value);
    }

    /// <summary>获取或设置搜索去抖时长。</summary>
    public TimeSpan SearchDelay
    {
        get => (TimeSpan)GetValue(SearchDelayProperty);
        set => SetValue(SearchDelayProperty, value);
    }

    /// <summary>获取或设置触发远程搜索的最少字符数。</summary>
    public int MinimumSearchLength
    {
        get => (int)GetValue(MinimumSearchLengthProperty);
        set => SetValue(MinimumSearchLengthProperty, value);
    }

    /// <summary>获取当前是否正在搜索。</summary>
    public bool IsSearching => (bool)GetValue(IsSearchingProperty);

    /// <summary>获取最近一次搜索错误文本。</summary>
    public string? SearchError => (string?)GetValue(SearchErrorProperty);

    /// <inheritdoc />
    public override void OnApplyTemplate()
    {
        DetachEditableTextBox();
        base.OnApplyTemplate();
        _editableTextBox = GetTemplateChild("PART_EditableTextBox") as TextBox;
        if (_editableTextBox is not null)
        {
            _editableTextBox.TextChanged += OnEditableTextChanged;
        }
    }

    /// <summary>立即执行一次搜索；常规输入由控件自动去抖调用。</summary>
    public async Task SearchAsync(string? searchText, CancellationToken cancellationToken = default)
    {
        if (Provider is not { } provider)
        {
            return;
        }

        var normalized = searchText?.Trim() ?? string.Empty;
        if (normalized.Length < MinimumSearchLength)
        {
            ItemsSource = Array.Empty<object>();
            IsDropDownOpen = false;
            SetValue(SearchErrorPropertyKey, null);
            return;
        }

        SetValue(IsSearchingPropertyKey, true);
        SetValue(SearchErrorPropertyKey, null);
        try
        {
            var results = await provider.SearchAsync(normalized, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var safeResults = results ?? Array.Empty<object>();
            ItemsSource = safeResults;
            ApplyLookupValue();
            IsDropDownOpen = safeResults.Count > 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            ItemsSource = Array.Empty<object>();
            IsDropDownOpen = false;
            SetValue(SearchErrorPropertyKey, exception.Message);
        }
        finally
        {
            SetValue(IsSearchingPropertyKey, false);
        }
    }

    /// <summary>使用 Provider 按当前 SelectedValue 回填显示项。</summary>
    public async Task ResolveSelectedValueAsync(CancellationToken cancellationToken = default)
    {
        if (Provider is not { } provider || LookupValue is null ||
            LookupValueAccessor.FindBySelectedValue(ItemsSource, LookupValue, SelectedValuePath) is not null)
        {
            return;
        }

        var item = await provider.ResolveAsync(LookupValue, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (item is not null)
        {
            ItemsSource = new[] { item };
            SelectedItem = item;
        }
    }

    private static void OnProviderChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        var editor = (GridLookupEditor)dependencyObject;
        editor.IsTextSearchEnabled = e.NewValue is null;
        editor.RestartResolve();
    }

    private static void OnLookupValueChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        var editor = (GridLookupEditor)dependencyObject;
        editor.ApplyLookupValue();
        editor.RestartResolve();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_isLoaded)
        {
            return;
        }

        _isLoaded = true;
        ApplyLookupValue();
        RestartResolve();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (!_isLoaded)
        {
            return;
        }

        _searchCancellation?.Cancel();
        _resolveCancellation?.Cancel();
        _isLoaded = false;
    }

    private void OnEditableTextChanged(object sender, TextChangedEventArgs e)
    {
        if (Provider is null)
        {
            return;
        }

        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        _searchCancellation = new CancellationTokenSource();
        _ = DebounceAndSearchAsync(_editableTextBox?.Text, _searchCancellation.Token);
    }

    private async Task DebounceAndSearchAsync(string? searchText, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(SearchDelay, cancellationToken);
            await SearchAsync(searchText, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    /// <inheritdoc />
    protected override void OnSelectionChanged(SelectionChangedEventArgs e)
    {
        base.OnSelectionChanged(e);
        if (SelectedItem is { } item)
        {
            SetCurrentValue(LookupValueProperty, LookupValueAccessor.GetValue(item, SelectedValuePath));
        }
    }

    /// <inheritdoc />
    protected override void OnItemsChanged(NotifyCollectionChangedEventArgs e)
    {
        base.OnItemsChanged(e);
        ApplyLookupValue();
    }

    private void ApplyLookupValue()
    {
        if (LookupValueAccessor.FindBySelectedValue(ItemsSource, LookupValue, SelectedValuePath) is { } item)
        {
            SelectedItem = item;
        }
        else if (LookupValue is null)
        {
            SelectedItem = null;
        }
    }

    private void RestartResolve()
    {
        _resolveCancellation?.Cancel();
        _resolveCancellation?.Dispose();
        _resolveCancellation = new CancellationTokenSource();
        _ = ResolveIgnoringCancellationAsync(_resolveCancellation.Token);
    }

    private async Task ResolveIgnoringCancellationAsync(CancellationToken cancellationToken)
    {
        try
        {
            await ResolveSelectedValueAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            SetValue(SearchErrorPropertyKey, exception.Message);
        }
    }

    private void DetachEditableTextBox()
    {
        if (_editableTextBox is not null)
        {
            _editableTextBox.TextChanged -= OnEditableTextChanged;
            _editableTextBox = null;
        }
    }
}
