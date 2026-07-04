using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace SwiftExportClient.Mvvm;

public sealed class BaseListModel<TLine, TDto> : ObservableObjectEx
    where TLine : BaseLineModel<TDto>
{
    private readonly List<TLine> _allLines = [];
    private readonly Func<TDto, TLine> _lineFactory;
    private TLine? _selectedItem;

    public BaseListModel(Func<TDto, TLine> lineFactory)
    {
        _lineFactory = lineFactory;
        Items.CollectionChanged += OnItemsCollectionChanged;
    }

    public ObservableCollection<TLine> Items { get; } = [];

    public TLine? SelectedItem
    {
        get => _selectedItem;
        set => SetProperty(ref _selectedItem, value);
    }

    public bool HasChanges => _allLines.Any(line => line.IsDirty);

    public int CheckedCount => Items.Count(line => line.IsChecked);

    public bool IsAllChecked
    {
        get => Items.Count > 0 && Items.All(line => line.IsChecked);
        set
        {
            foreach (var line in Items)
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

        _allLines.Clear();
        Items.Clear();

        foreach (var dto in dtos)
        {
            var line = _lineFactory(dto);
            line.Load(dto);
            AddLine(line);
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
        Items.Remove(line);
        Reindex();
        RaiseListStateChanged();
    }

    public void RemoveCompletely(TLine? line)
    {
        if (line is null)
        {
            return;
        }

        line.PropertyChanged -= OnLinePropertyChanged;
        Items.Remove(line);
        _allLines.Remove(line);
        Reindex();
        RaiseListStateChanged();
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

        Reindex();
        RaiseListStateChanged();
    }

    public void RejectChanges()
    {
        var added = _allLines.Where(line => line.State == LineState.Added).ToArray();
        foreach (var line in added)
        {
            RemoveCompletely(line);
        }

        var deleted = _allLines.Where(line => line.State == LineState.Deleted).ToArray();
        foreach (var line in deleted)
        {
            line.RestoreFromDeleted();
            if (!Items.Contains(line))
            {
                Items.Add(line);
            }
        }

        foreach (var line in _allLines.Where(line => line.State == LineState.Modified))
        {
            line.RejectChanges();
        }

        Reindex();
        RaiseListStateChanged();
    }

    public async Task<string?> ValidateAllAsync()
    {
        foreach (var line in Items)
        {
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
            Items[i].RowIndex = i + 1;
        }
    }

    private void AddLine(TLine line)
    {
        line.PropertyChanged += OnLinePropertyChanged;
        _allLines.Add(line);
        Items.Add(line);
        Reindex();
    }

    private void OnItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
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
