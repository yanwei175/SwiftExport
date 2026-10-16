using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;

namespace SwiftExportClient.Resources.Behaviors.Internal;

/// <summary>观察模型软错误并合并刷新；原生转换和页面保存校验各自保持原有职责。</summary>
internal sealed class GridValidationModule
{
    private readonly DataGrid _dataGrid;
    private readonly Dictionary<object, ItemSubscription> _itemSubscriptions =
        new(ReferenceEqualityComparer.Instance);
    private readonly object _refreshGate = new();
    private DispatcherOperation? _pendingRefresh;
    private bool _isAttached;
    private bool _isLoaded;
    private bool _isObserving;

    /// <summary>绑定唯一宿主，尚不订阅事件。</summary>
    public GridValidationModule(DataGrid dataGrid)
    {
        _dataGrid = dataGrid;
    }

    /// <summary>订阅加载卸载，已加载宿主立即启动当前能力。</summary>
    public void Attach()
    {
        if (_isAttached)
        {
            return;
        }

        _isAttached = true;
        _dataGrid.Loaded += OnLoaded;
        _dataGrid.Unloaded += OnUnloaded;
        _isLoaded = _dataGrid.IsLoaded;
        RefreshEnabledState();
    }

    /// <summary>解除宿主与运行期订阅，清理本适配拥有的状态。</summary>
    public void Detach()
    {
        if (!_isAttached)
        {
            return;
        }

        _dataGrid.Loaded -= OnLoaded;
        _dataGrid.Unloaded -= OnUnloaded;
        StopObserving();
        _isLoaded = false;
        _isAttached = false;
    }

    /// <summary>宿主重载时恢复当前数据源订阅。</summary>
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, _dataGrid)) return;
        _isLoaded = true;
        RefreshEnabledState();
    }

    /// <summary>宿主卸载时释放运行期订阅和排队工作。</summary>
    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, _dataGrid)) return;
        _isLoaded = false;
        StopObserving();
    }

    /// <summary>按当前开关启动或停止，卸载期间不观察数据。</summary>
    internal void RefreshEnabledState()
    {
        if (!_isAttached || !_isLoaded)
        {
            return;
        }

        if (GridValidation.GetIsEnabled(_dataGrid))
        {
            StartObserving();
        }
        else
        {
            StopObserving();
        }
    }

    /// <summary>观察当前视图和模型错误，不订阅原生验证事件。</summary>
    private void StartObserving()
    {
        if (_isObserving)
        {
            RefreshSubscriptionsAndErrors();
            return;
        }

        lock (_refreshGate) _isObserving = true;
        if (_dataGrid.Items is INotifyCollectionChanged collectionChanged)
        {
            collectionChanged.CollectionChanged += OnItemsCollectionChanged;
        }

        RefreshSubscriptionsAndErrors();
    }

    /// <summary>释放行和视图订阅，只清空呈现快照，不清除模型错误。</summary>
    private void StopObserving()
    {
        if (!_isObserving)
        {
            return;
        }

        if (_dataGrid.Items is INotifyCollectionChanged collectionChanged)
        {
            collectionChanged.CollectionChanged -= OnItemsCollectionChanged;
        }

        lock (_refreshGate)
        {
            _isObserving = false;
            _pendingRefresh?.Abort();
            _pendingRefresh = null;
        }
        foreach (var subscription in _itemSubscriptions.Values)
        {
            subscription.Dispose();
        }

        _itemSubscriptions.Clear();
        GridValidation.SetErrorsFromView(_dataGrid, Array.Empty<GridValidationError>());
    }

    /// <summary>按当前视图差量维护行订阅，替换源或移除行后解除旧引用。</summary>
    private void RefreshSubscriptionsAndErrors()
    {
        var currentItems = _dataGrid.Items
            .Cast<object?>()
            .Where(IsDataItem)
            .Cast<object>()
            .Where(item => item is IDataErrorInfo)
            .ToHashSet(ReferenceEqualityComparer.Instance);

        foreach (var removedItem in _itemSubscriptions.Keys.Where(item => !currentItems.Contains(item)).ToArray())
        {
            _itemSubscriptions[removedItem].Dispose();
            _itemSubscriptions.Remove(removedItem);
        }

        foreach (var item in currentItems)
        {
            if (!_itemSubscriptions.ContainsKey(item))
            {
                _itemSubscriptions.Add(item, new ItemSubscription(item, OnItemPropertyChanged));
            }
        }

        QueueRefresh();
    }

    /// <summary>合并同一调度周期内的通知，允许后台错误通知；卸载时取消排队工作。</summary>
    private void QueueRefresh()
    {
        lock (_refreshGate)
        {
            if (!_isObserving || _pendingRefresh is not null) return;
            _pendingRefresh = _dataGrid.Dispatcher.BeginInvoke(DispatcherPriority.DataBind, new Action(() =>
            {
                lock (_refreshGate)
                {
                    _pendingRefresh = null;
                    if (!_isObserving) return;
                }
                RefreshErrors();
            }));
        }
    }

    /// <summary>只在宿主线程读取当前模型并发布去重快照，不承诺错误的展示顺序。</summary>
    private void RefreshErrors()
    {
        if (!_isObserving)
        {
            return;
        }

        var errors = new List<GridValidationError>();
        foreach (var item in _dataGrid.Items.Cast<object?>().Where(IsDataItem).Cast<object>())
        {
            AddItemErrors(errors, item);
        }

        var snapshot = errors
            .Where(error => !string.IsNullOrWhiteSpace(error.Message))
            .Distinct(GridValidationErrorComparer.Instance)
            .ToArray();
        GridValidation.SetErrorsFromView(_dataGrid, snapshot);
    }

    /// <summary>从 IDataErrorInfo 读取行与字段软错误，业务校验仍由模型负责。</summary>
    private void AddItemErrors(List<GridValidationError> target, object item)
    {
        if (item is not IDataErrorInfo dataErrorInfo)
        {
            return;
        }

        AddMessage(target, item, null, dataErrorInfo.Error);
        foreach (var column in _dataGrid.Columns)
        {
            var fieldPath = GetFieldPath(column);
            if (!string.IsNullOrWhiteSpace(fieldPath))
            {
                AddMessage(target, item, column, dataErrorInfo[fieldPath]);
            }
        }
    }

    /// <summary>规范化非空文本并关联数据项和列，不创建原生 ValidationError。</summary>
    private static void AddMessage(
        List<GridValidationError> target,
        object item,
        DataGridColumn? column,
        string? message)
    {
        var normalized = message?.Trim();
        if (!string.IsNullOrWhiteSpace(normalized))
        {
            target.Add(new GridValidationError(item, column, normalized));
        }
    }

    /// <summary>模板映射优先，其余只读取原生绑定路径；不猜测显示或填充字段。</summary>
    private static string? GetFieldPath(DataGridColumn column)
    {
        var configured = GridValidation.GetFieldPath(column);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        return column is DataGridBoundColumn { Binding: Binding binding }
            ? binding.Path?.Path
            : null;
    }

    private void OnItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshSubscriptionsAndErrors();
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e) => QueueRefresh();

    private static bool IsDataItem(object? item) =>
        item is not null &&
        !ReferenceEquals(item, CollectionView.NewItemPlaceholder) &&
        item is not CollectionViewGroup;

    /// <summary>观察 IDataErrorInfo 模型的属性通知，释放行时成对解绑。</summary>
    private sealed class ItemSubscription : IDisposable
    {
        private readonly INotifyPropertyChanged? _propertyChanged;
        private readonly PropertyChangedEventHandler _changed;

        /// <summary>仅观察当前模型的属性通知，无通知接口的模型只在初次加载或视图变化时读取。</summary>
        public ItemSubscription(object item, PropertyChangedEventHandler changed)
        {
            _changed = changed;
            _propertyChanged = item as INotifyPropertyChanged;
            if (_propertyChanged is not null) _propertyChanged.PropertyChanged += _changed;
        }

        /// <summary>解除此行的属性通知订阅。</summary>
        public void Dispose()
        {
            if (_propertyChanged is not null) _propertyChanged.PropertyChanged -= _changed;
        }
    }

    private sealed class GridValidationErrorComparer : IEqualityComparer<GridValidationError>
    {
        public static GridValidationErrorComparer Instance { get; } = new();

        public bool Equals(GridValidationError? x, GridValidationError? y) =>
            ReferenceEquals(x?.Item, y?.Item) &&
            ReferenceEquals(x?.Column, y?.Column) &&
            string.Equals(x?.Message, y?.Message, StringComparison.Ordinal);

        public int GetHashCode(GridValidationError value) =>
            HashCode.Combine(
                value.Item is null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value.Item),
                value.Column,
                value.Message);
    }
}
