namespace SwiftExportClient.Mvvm;

public abstract class BaseLineModel<TDto> : ObservableObjectEx
{
    private TDto? _originalDto;
    private LineState _state;
    private bool _isChecked;
    private bool _isSelected;
    private bool _isReadOnly;
    private int _rowIndex;

    public LineState State
    {
        get => _state;
        private set
        {
            if (SetProperty(ref _state, value))
            {
                OnPropertyChanged(nameof(IsDirty));
                OnPropertyChanged(nameof(IsNew));
                OnPropertyChanged(nameof(IsModified));
                OnPropertyChanged(nameof(IsDeleted));
            }
        }
    }

    public bool IsDirty => State is LineState.Added or LineState.Modified or LineState.Deleted;

    public bool IsNew => State == LineState.Added;

    public bool IsModified => State == LineState.Modified;

    public bool IsDeleted => State == LineState.Deleted;

    public bool IsChecked
    {
        get => _isChecked;
        set => SetProperty(ref _isChecked, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public bool IsReadOnly
    {
        get => _isReadOnly;
        set => SetProperty(ref _isReadOnly, value);
    }

    public int RowIndex
    {
        get => _rowIndex;
        internal set => SetProperty(ref _rowIndex, value);
    }

    public void Load(TDto dto)
    {
        using var _ = SuppressScope();
        LoadFromDto(dto);
        _originalDto = dto;
        State = LineState.Unchanged;
    }

    public void MarkAsAdded()
    {
        State = LineState.Added;
    }

    public void MarkAsModified()
    {
        if (State == LineState.Unchanged)
        {
            State = LineState.Modified;
        }
    }

    public void MarkAsDeleted()
    {
        State = LineState.Deleted;
    }

    public void RestoreFromDeleted()
    {
        State = _originalDto is null ? LineState.Added : LineState.Unchanged;
    }

    public void AcceptChanges()
    {
        _originalDto = ToDto();
        State = LineState.Unchanged;
    }

    public void RejectChanges()
    {
        if (_originalDto is not null)
        {
            using var _ = SuppressScope();
            LoadFromDto(_originalDto);
            State = LineState.Unchanged;
            return;
        }

        State = LineState.Added;
    }

    public virtual Task<string?> ValidateAsync()
    {
        return Task.FromResult<string?>(null);
    }

    protected bool SetTrackedProperty<T>(ref T field, T value, string? propertyName = null)
    {
        if (!SetProperty(ref field, value, propertyName))
        {
            return false;
        }

        if (!IsSuppressed)
        {
            MarkAsModified();
        }

        return true;
    }

    protected abstract void LoadFromDto(TDto dto);

    public abstract TDto ToDto();
}
