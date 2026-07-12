using System.ComponentModel;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace SwiftExportClient.Mvvm;

public abstract class BaseLineModel<TDto> : ObservableObjectEx
{
    private static readonly ConcurrentDictionary<Type, PropertyDescriptor[]> WritableProperties = [];
    private readonly HashSet<string> _changingProperties = new(StringComparer.Ordinal);
    private readonly Dictionary<string, object?> _baselineValues = new(StringComparer.Ordinal);
    private TDto? _originalDto;
    private LineState _state;
    private LineState? _stateBeforeDelete;
    private bool _isChecked;
    private bool _isSelected;
    private bool _isReadOnly;
    private int _rowIndex;

    protected BaseLineModel()
    {
        PropertyChanging += OnTrackedPropertyChanging;
        PropertyChanged += OnTrackedPropertyChanged;
    }

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
        _originalDto = ToDto();
        CaptureBaselineValues();
        _stateBeforeDelete = null;
        State = LineState.Unchanged;
    }

    public void MarkAsAdded()
    {
        _stateBeforeDelete = null;
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
        if (State == LineState.Deleted)
        {
            return;
        }

        _stateBeforeDelete = State;
        State = LineState.Deleted;
    }

    public void RestoreFromDeleted()
    {
        if (State != LineState.Deleted)
        {
            return;
        }

        State = _stateBeforeDelete ?? (_originalDto is null ? LineState.Added : LineState.Unchanged);
        _stateBeforeDelete = null;
    }

    public void AcceptChanges()
    {
        _originalDto = ToDto();
        CaptureBaselineValues();
        _stateBeforeDelete = null;
        State = LineState.Unchanged;
    }

    public void RejectChanges()
    {
        if (_originalDto is not null)
        {
            using var _ = SuppressScope();
            LoadFromDto(_originalDto);
            _stateBeforeDelete = null;
            State = LineState.Unchanged;
            return;
        }

        _stateBeforeDelete = null;
        State = LineState.Added;
    }

    public virtual string? ValidateRow()
    {
        return null;
    }

    public virtual Task<string?> ValidateAsync()
    {
        return Task.FromResult<string?>(null);
    }

    protected bool SetTrackedProperty<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        return SetProperty(ref field, value, propertyName);
    }

    protected virtual bool IsTrackingIgnoredProperty(string? propertyName)
    {
        return propertyName is null
            or nameof(State)
            or nameof(IsDirty)
            or nameof(IsNew)
            or nameof(IsModified)
            or nameof(IsDeleted)
            or nameof(IsChecked)
            or nameof(IsSelected)
            or nameof(IsReadOnly)
            or nameof(RowIndex)
            or nameof(IsSuppressed);
    }

    protected abstract void LoadFromDto(TDto dto);

    public abstract TDto ToDto();

    private void OnTrackedPropertyChanging(object? sender, PropertyChangingEventArgs e)
    {
        if (IsSuppressed
            || State == LineState.Deleted
            || IsTrackingIgnoredProperty(e.PropertyName))
        {
            return;
        }

        _changingProperties.Add(e.PropertyName!);
    }

    private void OnTrackedPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null)
        {
            _changingProperties.Clear();
            return;
        }

        if (!_changingProperties.Remove(e.PropertyName)
            || IsSuppressed
            || State == LineState.Deleted)
        {
            return;
        }

        ReconcileModifiedState();
    }

    private void CaptureBaselineValues()
    {
        _baselineValues.Clear();
        foreach (var property in GetTrackableProperties())
        {
            _baselineValues[property.Name] = property.GetValue(this);
        }
    }

    private void ReconcileModifiedState()
    {
        if (State is LineState.Added or LineState.Deleted)
        {
            return;
        }

        foreach (var property in GetTrackableProperties())
        {
            if (!_baselineValues.TryGetValue(property.Name, out var baseline) ||
                !Equals(baseline, property.GetValue(this)))
            {
                State = LineState.Modified;
                return;
            }
        }

        State = LineState.Unchanged;
    }

    private IEnumerable<PropertyDescriptor> GetTrackableProperties()
    {
        var properties = WritableProperties.GetOrAdd(
            GetType(),
            static type => TypeDescriptor.GetProperties(type)
                .Cast<PropertyDescriptor>()
                .Where(property => !property.IsReadOnly)
                .ToArray());
        return properties.Where(property => !IsTrackingIgnoredProperty(property.Name));
    }
}
