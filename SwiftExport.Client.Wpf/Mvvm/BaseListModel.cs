using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace SwiftExportClient.Mvvm;

public class BaseListModel<TLine, TDto> : ObservableObjectEx
    where TLine : BaseLineModel<TDto>
{
    private readonly List<TLine> _allLines = [];
    private readonly ObservableCollection<TLine> _items = [];
    private readonly Func<TDto, TLine> _lineFactory;
    private TLine? _selectedItem;
    private bool _isUpdatingItems;

    public BaseListModel(Func<TDto, TLine> lineFactory)
    {
        _lineFactory = lineFactory;
        Items = new ReadOnlyObservableCollection<TLine>(_items);
        _items.CollectionChanged += OnItemsCollectionChanged;
    }

    public ReadOnlyObservableCollection<TLine> Items { get; }

    public TLine? SelectedItem
    {
        get => _selectedItem;
        set => SetProperty(ref _selectedItem, value);
    }

    public bool HasChanges => _allLines.Any(line => line.IsDirty);

    public int CheckedCount => _items.Count(line => line.IsChecked);

    public bool IsAllChecked
    {
        get => _items.Count > 0 && _items.All(line => line.IsChecked);
        set
        {
            foreach (var line in _items)
            {
                line.IsChecked = value;
            }

            RaiseListStateChanged();
        }
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
        foreach (var line in _allLines)
        {
            line.PropertyChanged -= OnLinePropertyChanged;
        }

        _isUpdatingItems = true;
        try
        {
            _allLines.Clear();
            _items.Clear();

            foreach (var dto in dtos)
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
            RaiseListStateChanged();
        }
    }

    public GridChangeSet<TDto> GetChangeSet()
    {
        return new GridChangeSet<TDto>(
            _allLines.Where(line => line.State == LineState.Added).Select(line => line.ToDto()).ToArray(),
            _allLines.Where(line => line.State == LineState.Modified).Select(line => line.ToDto()).ToArray(),
            _allLines.Where(line => line.State == LineState.Deleted).Select(line => line.ToDto()).ToArray());
    }

    public void AcceptChanges()
    {
        var deleted = _allLines.Where(line => line.State == LineState.Deleted).ToArray();
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
        var added = _allLines.Where(line => line.State == LineState.Added).ToArray();
        foreach (var line in added)
        {
            line.PropertyChanged -= OnLinePropertyChanged;
            _allLines.Remove(line);
        }

        var deleted = _allLines.Where(line => line.State == LineState.Deleted).ToArray();
        foreach (var line in deleted)
        {
            line.RestoreFromDeleted();
        }

        foreach (var line in _allLines.Where(line => line.State == LineState.Modified))
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
        if (e.PropertyName is nameof(BaseLineModel<TDto>.IsDirty) or nameof(BaseLineModel<TDto>.IsChecked))
        {
            RaiseListStateChanged();
        }
    }

    private void RaiseListStateChanged()
    {
        OnPropertyChanged(nameof(HasChanges));
        OnPropertyChanged(nameof(CheckedCount));
        OnPropertyChanged(nameof(IsAllChecked));
    }
}
