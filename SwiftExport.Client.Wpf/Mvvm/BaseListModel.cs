using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using SwiftExport.Contracts.DTOs.Base;

namespace SwiftExportClient.Mvvm;

public sealed class ListStateChangedEventArgs<TLine>(TLine? line, string? propertyName) : EventArgs
{
    public TLine? Line { get; } = line;

    public string? PropertyName { get; } = propertyName;
}

public partial class BaseListModel<TLine, TDto> : ObservableObjectEx
    where TLine : BaseLineModel<TDto>
    where TDto : BaseDto
{
    private readonly List<TLine> _allLines = [];
    private readonly ObservableCollection<TLine> _items = [];
    private readonly Func<TDto, TLine> _lineFactory;
    private bool _isUpdatingItems;
    private int _listEventSuppressionCount;
    private bool _hasPendingListStateChange;

    public BaseListModel(Func<TDto, TLine> lineFactory)
    {
        _lineFactory = lineFactory;
        Items = new ReadOnlyObservableCollection<TLine>(_items);
        _items.CollectionChanged += OnItemsCollectionChanged;
    }

    public event EventHandler<ListStateChangedEventArgs<TLine>>? ListStateChanged;

    public ReadOnlyObservableCollection<TLine> Items { get; }

    public IReadOnlyList<TLine> AllLines => _allLines.ToArray();

    public IReadOnlyList<TLine> CheckedItems => _items.Where(line => line.IsChecked).ToArray();

    public IReadOnlyList<TLine> AddedItems => _allLines.Where(line => line.State == LineState.Added).ToArray();

    public IReadOnlyList<TLine> ModifiedItems => _allLines.Where(line => line.State == LineState.Modified).ToArray();

    public IReadOnlyList<TLine> DeletedItems => _allLines.Where(line => line.State == LineState.Deleted).ToArray();

    public IReadOnlyList<TLine> GetAddedLines() => AddedItems;

    public IReadOnlyList<TLine> GetModifiedLines() => ModifiedItems;

    public IReadOnlyList<TLine> GetDeletedLines() => DeletedItems;

    public IReadOnlyList<TLine> GetChangedLines() => _allLines.Where(line => line.IsDirty).ToArray();

    [ObservableProperty]
    public partial TLine? SelectedItem { get; set; }

    public bool HasChanges => _allLines.Any(line => line.IsDirty);

    public int CheckedCount => _items.Count(line => line.IsChecked);

    public bool IsAllChecked
    {
        get => _items.Count > 0 && _items.All(line => line.IsChecked);
        set
        {
            if (value)
            {
                CheckAll();
            }
            else
            {
                UncheckAll();
            }
        }
    }

    /// <summary>
    /// 嵌套安全地合并列表状态通知，最外层作用域结束时统一发布一次。
    /// </summary>
    public IDisposable SuppressListEvents()
    {
        _listEventSuppressionCount++;
        return new ListEventSuppression(this);
    }

    public TLine AddNew(TDto dto)
    {
        var line = _lineFactory(dto);
        line.Load(dto);
        line.MarkAsAdded();
        AddLine(line);
        return line;
    }

    public void LoadFromDtos(IEnumerable<TDto> dtos)
    {
        ArgumentNullException.ThrowIfNull(dtos);
        var materializedDtos = dtos.ToArray();

        using var _ = SuppressListEvents();
        _isUpdatingItems = true;
        try
        {
            ClearCore();
            foreach (var dto in materializedDtos)
            {
                var line = _lineFactory(dto);
                line.Load(dto);
                AddLine(line);
            }
        }
        finally
        {
            _isUpdatingItems = false;
        }

        Reindex();
        RaiseListStateChanged();
    }

    public void Clear()
    {
        using var _ = SuppressListEvents();
        _isUpdatingItems = true;
        try
        {
            ClearCore();
        }
        finally
        {
            _isUpdatingItems = false;
        }

        RaiseListStateChanged();
    }

    public void CheckAll() => SetAllChecked(true);

    public void UncheckAll() => SetAllChecked(false);

    public void ToggleCheckAll() => SetAllChecked(!IsAllChecked);

    public void InvertCheckAll()
    {
        using var _ = SuppressListEvents();
        foreach (var line in _items)
        {
            line.IsChecked = !line.IsChecked;
        }

        RaiseListStateChanged();
    }

    public void Remove(TLine? line)
    {
        if (line is null)
        {
            return;
        }

        if (line.IsNew)
        {
            RemoveCompletely(line);
            return;
        }

        line.MarkAsDeleted();
        if (ReferenceEquals(SelectedItem, line))
        {
            SelectedItem = null;
        }

        _items.Remove(line);
    }

    public void RemoveCompletely(TLine? line)
    {
        if (line is null)
        {
            return;
        }

        line.PropertyChanged -= OnLinePropertyChanged;
        if (ReferenceEquals(SelectedItem, line))
        {
            SelectedItem = null;
        }

        var wasVisible = _items.Remove(line);
        _allLines.Remove(line);
        if (!wasVisible)
        {
            Reindex();
            RaiseListStateChanged(line);
        }
    }

    public GridChangeSet<TDto> GetChangeSet()
    {
        return new GridChangeSet<TDto>(
            AddedItems.Select(line => line.ToDto()).ToArray(),
            ModifiedItems.Select(line => line.ToDto()).ToArray(),
            DeletedItems.Select(line => line.ToDto()).ToArray());
    }

    public void AcceptChanges()
    {
        using var _ = SuppressListEvents();
        var deleted = DeletedItems.ToArray();
        foreach (var line in deleted)
        {
            line.PropertyChanged -= OnLinePropertyChanged;
            _allLines.Remove(line);
        }

        foreach (var line in _allLines)
        {
            line.AcceptChanges();
        }

        if (SelectedItem is not null && !_items.Contains(SelectedItem))
        {
            SelectedItem = null;
        }

        Reindex();
        RaiseListStateChanged();
    }

    public void RejectChanges()
    {
        using var _ = SuppressListEvents();
        var added = AddedItems.ToArray();
        foreach (var line in added)
        {
            line.PropertyChanged -= OnLinePropertyChanged;
            _allLines.Remove(line);
        }

        foreach (var line in DeletedItems.ToArray())
        {
            line.RestoreFromDeleted();
        }

        foreach (var line in ModifiedItems.ToArray())
        {
            line.RejectChanges();
        }

        _isUpdatingItems = true;
        try
        {
            _items.Clear();
            foreach (var line in _allLines)
            {
                _items.Add(line);
            }
        }
        finally
        {
            _isUpdatingItems = false;
        }

        if (SelectedItem is not null && !_items.Contains(SelectedItem))
        {
            SelectedItem = null;
        }

        Reindex();
        RaiseListStateChanged();
    }

    public async Task<string?> ValidateAllAsync()
    {
        foreach (var line in Items)
        {
            var rowError = line.ValidateRow();
            if (!string.IsNullOrWhiteSpace(rowError))
            {
                return $"第 {line.RowIndex} 行：{rowError}";
            }

            var error = await line.ValidateAsync();
            if (!string.IsNullOrWhiteSpace(error))
            {
                return $"第 {line.RowIndex} 行：{error}";
            }
        }

        return null;
    }

    public void Reindex()
    {
        for (var i = 0; i < Items.Count; i++)
        {
            _items[i].RowIndex = i + 1;
        }
    }

    private void AddLine(TLine line)
    {
        line.PropertyChanged += OnLinePropertyChanged;
        _allLines.Add(line);
        _items.Add(line);
    }

    private void ClearCore()
    {
        foreach (var line in _allLines)
        {
            line.PropertyChanged -= OnLinePropertyChanged;
        }

        _allLines.Clear();
        _items.Clear();
        SelectedItem = null;
    }

    private void SetAllChecked(bool value)
    {
        using var _ = SuppressListEvents();
        foreach (var line in _items)
        {
            line.IsChecked = value;
        }

        RaiseListStateChanged();
    }

    private void OnItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_isUpdatingItems)
        {
            return;
        }

        Reindex();
        RaiseListStateChanged();
    }

    private void OnLinePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(BaseLineModel<TDto>.IsDirty)
            or nameof(BaseLineModel<TDto>.IsNew)
            or nameof(BaseLineModel<TDto>.IsModified)
            or nameof(BaseLineModel<TDto>.IsDeleted))
        {
            return;
        }

        RaiseListStateChanged(sender as TLine, e.PropertyName);
    }

    private void RaiseListStateChanged(TLine? line = null, string? propertyName = null)
    {
        if (_listEventSuppressionCount > 0)
        {
            _hasPendingListStateChange = true;
            return;
        }

        PublishListStateChanged(line, propertyName);
    }

    private void PublishListStateChanged(TLine? line, string? propertyName)
    {
        OnPropertyChanged(nameof(AllLines));
        OnPropertyChanged(nameof(CheckedItems));
        OnPropertyChanged(nameof(AddedItems));
        OnPropertyChanged(nameof(ModifiedItems));
        OnPropertyChanged(nameof(DeletedItems));
        OnPropertyChanged(nameof(HasChanges));
        OnPropertyChanged(nameof(CheckedCount));
        OnPropertyChanged(nameof(IsAllChecked));
        ListStateChanged?.Invoke(this, new ListStateChangedEventArgs<TLine>(line, propertyName));
    }

    private void EndListEventSuppression()
    {
        if (_listEventSuppressionCount <= 0 || --_listEventSuppressionCount > 0)
        {
            return;
        }

        if (_hasPendingListStateChange)
        {
            _hasPendingListStateChange = false;
            PublishListStateChanged(null, null);
        }
    }

    private sealed class ListEventSuppression(BaseListModel<TLine, TDto> owner) : IDisposable
    {
        private BaseListModel<TLine, TDto>? _owner = owner;

        public void Dispose()
        {
            Interlocked.Exchange(ref _owner, null)?.EndListEventSuppression();
        }
    }
}
