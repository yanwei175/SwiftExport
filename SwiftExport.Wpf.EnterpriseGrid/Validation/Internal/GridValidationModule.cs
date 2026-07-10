using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using SwiftExport.Wpf.EnterpriseGrid.Columns;

namespace SwiftExport.Wpf.EnterpriseGrid.Validation.Internal;

internal sealed class GridValidationModule
{
    private readonly DataGrid _dataGrid;
    private readonly Dictionary<object, ItemSubscription> _itemSubscriptions =
        new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<System.Windows.Controls.ValidationError, GridValidationError> _visualErrors = [];
    private bool _isAttached;
    private bool _isObserving;

    public GridValidationModule(DataGrid dataGrid)
    {
        _dataGrid = dataGrid;
    }

    public void Attach()
    {
        if (_isAttached)
        {
            return;
        }

        _isAttached = true;
        RefreshEnabledState();
    }

    public void Detach()
    {
        if (!_isAttached)
        {
            return;
        }

        StopObserving();
        _isAttached = false;
    }

    internal void RefreshEnabledState()
    {
        if (!_isAttached)
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

    internal bool FocusFirstError()
    {
        if (!_isObserving || GridValidation.GetFirstError(_dataGrid) is not { } error ||
            error.Item is null || !_dataGrid.Items.Contains(error.Item))
        {
            return false;
        }

        _dataGrid.SelectedItem = error.Item;
        if (!string.IsNullOrWhiteSpace(error.ColumnKey) && GridColumn.FindByKey(_dataGrid, error.ColumnKey) is { } column)
        {
            _dataGrid.ScrollIntoView(error.Item, column);
            _dataGrid.CurrentCell = new DataGridCellInfo(error.Item, column);
        }
        else
        {
            _dataGrid.ScrollIntoView(error.Item);
        }

        _dataGrid.Focus();
        return true;
    }

    private void StartObserving()
    {
        if (_isObserving)
        {
            RefreshSubscriptionsAndErrors();
            return;
        }

        _isObserving = true;
        if (_dataGrid.Items is INotifyCollectionChanged collectionChanged)
        {
            collectionChanged.CollectionChanged += OnItemsCollectionChanged;
        }

        System.Windows.Controls.Validation.AddErrorHandler(_dataGrid, OnValidationError);
        RefreshSubscriptionsAndErrors();
    }

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

        System.Windows.Controls.Validation.RemoveErrorHandler(_dataGrid, OnValidationError);
        foreach (var subscription in _itemSubscriptions.Values)
        {
            subscription.Dispose();
        }

        _itemSubscriptions.Clear();
        _visualErrors.Clear();
        _isObserving = false;
        GridValidation.SetErrorsFromView(_dataGrid, Array.Empty<GridValidationError>());
    }

    private void RefreshSubscriptionsAndErrors()
    {
        var currentItems = _dataGrid.Items
            .Cast<object?>()
            .Where(IsDataItem)
            .Cast<object>()
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
                _itemSubscriptions.Add(item, new ItemSubscription(item, OnItemErrorsChanged));
            }
        }

        RefreshErrors();
    }

    private void RefreshErrors()
    {
        if (!_dataGrid.Dispatcher.CheckAccess())
        {
            _dataGrid.Dispatcher.BeginInvoke(RefreshErrors);
            return;
        }

        if (!_isObserving)
        {
            return;
        }

        var errors = new List<GridValidationError>(_visualErrors.Values);
        foreach (var item in _dataGrid.Items.Cast<object?>().Where(IsDataItem).Cast<object>())
        {
            AddItemErrors(errors, item);
        }

        var ordered = errors
            .Where(error => !string.IsNullOrWhiteSpace(error.Message))
            .Distinct(GridValidationErrorComparer.Instance)
            .OrderBy(GetRowOrder)
            .ThenBy(GetColumnOrder)
            .ThenBy(error => error.Message, StringComparer.CurrentCulture)
            .ToArray();
        GridValidation.SetErrorsFromView(_dataGrid, ordered);
    }

    private void AddItemErrors(List<GridValidationError> target, object item)
    {
        if (item is INotifyDataErrorInfo notifyDataErrorInfo && notifyDataErrorInfo.HasErrors)
        {
            var propertyErrorStart = target.Count;
            foreach (var column in _dataGrid.Columns.OrderBy(column => column.DisplayIndex))
            {
                var fieldPath = GetFieldPath(column);
                if (!string.IsNullOrWhiteSpace(fieldPath))
                {
                    AddMessages(target, item, GridColumn.GetKey(column), notifyDataErrorInfo.GetErrors(fieldPath));
                }
            }

            var propertyMessages = target
                .Skip(propertyErrorStart)
                .Select(error => error.Message)
                .ToHashSet(StringComparer.Ordinal);
            AddMessages(
                target,
                item,
                null,
                notifyDataErrorInfo.GetErrors(null),
                propertyMessages);
        }

        if (item is not IDataErrorInfo dataErrorInfo)
        {
            return;
        }

        AddMessage(target, item, null, dataErrorInfo.Error);
        foreach (var column in _dataGrid.Columns.OrderBy(column => column.DisplayIndex))
        {
            var fieldPath = GetFieldPath(column);
            if (!string.IsNullOrWhiteSpace(fieldPath))
            {
                AddMessage(target, item, GridColumn.GetKey(column), dataErrorInfo[fieldPath]);
            }
        }
    }

    private static void AddMessages(
        List<GridValidationError> target,
        object item,
        string? columnKey,
        IEnumerable? messages,
        IReadOnlySet<string>? excludedMessages = null)
    {
        if (messages is null)
        {
            return;
        }

        if (messages is string text)
        {
            AddMessage(target, item, columnKey, text, excludedMessages);
            return;
        }

        foreach (var message in messages)
        {
            AddMessage(target, item, columnKey, message?.ToString(), excludedMessages);
        }
    }

    private static void AddMessage(
        List<GridValidationError> target,
        object item,
        string? columnKey,
        string? message,
        IReadOnlySet<string>? excludedMessages = null)
    {
        var normalized = message?.Trim();
        if (!string.IsNullOrWhiteSpace(normalized) && excludedMessages?.Contains(normalized) != true)
        {
            target.Add(new GridValidationError(item, columnKey, normalized));
        }
    }

    private string? GetFieldPath(DataGridColumn column)
    {
        var configured = GridColumn.GetFieldPath(column);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        return column is DataGridBoundColumn { Binding: Binding binding }
            ? binding.Path?.Path
            : null;
    }

    private int GetRowOrder(GridValidationError error)
    {
        var index = error.Item is null ? -1 : _dataGrid.Items.IndexOf(error.Item);
        return index < 0 ? int.MaxValue : index;
    }

    private int GetColumnOrder(GridValidationError error)
    {
        if (string.IsNullOrWhiteSpace(error.ColumnKey))
        {
            return -1;
        }

        return GridColumn.FindByKey(_dataGrid, error.ColumnKey)?.DisplayIndex ?? int.MaxValue;
    }

    private void OnValidationError(object? sender, ValidationErrorEventArgs e)
    {
        if (e.Action == ValidationErrorEventAction.Added)
        {
            _visualErrors[e.Error] = CreateVisualError(e);
        }
        else
        {
            _visualErrors.Remove(e.Error);
        }

        RefreshErrors();
    }

    private static GridValidationError CreateVisualError(ValidationErrorEventArgs e)
    {
        var source = e.OriginalSource as DependencyObject;
        var cell = FindAncestor<DataGridCell>(source);
        var row = FindAncestor<DataGridRow>(source);
        var item = row?.Item ?? (source as FrameworkElement)?.DataContext;
        var columnKey = cell?.Column is null ? null : GridColumn.GetKey(cell.Column);
        var message = e.Error.ErrorContent?.ToString();
        if (string.IsNullOrWhiteSpace(message) && e.Error.Exception is { } exception)
        {
            message = exception.Message;
        }

        return new GridValidationError(item, columnKey, message?.Trim() ?? "数据无效");
    }

    private void OnItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshSubscriptionsAndErrors();
    }

    private void OnItemErrorsChanged(object? sender, EventArgs e)
    {
        RefreshErrors();
    }

    private static bool IsDataItem(object? item) =>
        item is not null &&
        !ReferenceEquals(item, CollectionView.NewItemPlaceholder) &&
        item is not CollectionViewGroup;

    private static T? FindAncestor<T>(DependencyObject? source)
        where T : DependencyObject
    {
        var current = source;
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = current switch
            {
                Visual or Visual3D => VisualTreeHelper.GetParent(current),
                FrameworkContentElement contentElement => contentElement.Parent,
                _ => LogicalTreeHelper.GetParent(current)
            };
        }

        return null;
    }

    private sealed class ItemSubscription : IDisposable
    {
        private readonly INotifyDataErrorInfo? _dataErrorInfo;
        private readonly INotifyPropertyChanged? _propertyChanged;
        private readonly EventHandler _changed;
        private readonly EventHandler<DataErrorsChangedEventArgs> _errorsChanged;
        private readonly PropertyChangedEventHandler _propertyChangedHandler;

        public ItemSubscription(object item, EventHandler changed)
        {
            _changed = changed;
            _errorsChanged = OnErrorsChanged;
            _propertyChangedHandler = OnPropertyChanged;
            _dataErrorInfo = item as INotifyDataErrorInfo;
            _propertyChanged = item as INotifyPropertyChanged;
            if (_dataErrorInfo is not null)
            {
                _dataErrorInfo.ErrorsChanged += _errorsChanged;
            }

            if (_propertyChanged is not null)
            {
                _propertyChanged.PropertyChanged += _propertyChangedHandler;
            }
        }

        public void Dispose()
        {
            if (_dataErrorInfo is not null)
            {
                _dataErrorInfo.ErrorsChanged -= _errorsChanged;
            }

            if (_propertyChanged is not null)
            {
                _propertyChanged.PropertyChanged -= _propertyChangedHandler;
            }
        }

        private void OnErrorsChanged(object? sender, DataErrorsChangedEventArgs e) => _changed(sender, e);

        private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e) => _changed(sender, e);
    }

    private sealed class GridValidationErrorComparer : IEqualityComparer<GridValidationError>
    {
        public static GridValidationErrorComparer Instance { get; } = new();

        public bool Equals(GridValidationError? x, GridValidationError? y) =>
            ReferenceEquals(x?.Item, y?.Item) &&
            string.Equals(x?.ColumnKey, y?.ColumnKey, StringComparison.Ordinal) &&
            string.Equals(x?.Message, y?.Message, StringComparison.Ordinal);

        public int GetHashCode(GridValidationError value) =>
            HashCode.Combine(
                value.Item is null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value.Item),
                value.ColumnKey,
                value.Message);
    }
}
