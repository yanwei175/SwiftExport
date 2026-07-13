using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.ComponentModel;
using SwiftExport.Contracts.DTOs.Base;

namespace SwiftExportClient.Mvvm;

public abstract partial class BaseLineModel<TDto> : ObservableObjectEx
    where TDto : BaseDto
{
    private static readonly ConcurrentDictionary<Type, PropertyDescriptor[]> WritableProperties = [];
    private readonly HashSet<string> _changingProperties = new(StringComparer.Ordinal);
    private readonly Dictionary<string, object?> _baselineValues = new(StringComparer.Ordinal);
    private TDto? _originalDto;
    private LineState? _stateBeforeDelete;
    private readonly HashSet<string> _suppressedChangedProperties = new(StringComparer.Ordinal);
    private int _changeTrackingSuppressionCount;

    protected BaseLineModel()
    {
        PropertyChanging += OnTrackedPropertyChanging;
        PropertyChanged += OnTrackedPropertyChanged;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    [NotifyPropertyChangedFor(nameof(IsNew))]
    [NotifyPropertyChangedFor(nameof(IsModified))]
    [NotifyPropertyChangedFor(nameof(IsDeleted))]
    public partial LineState State { get; private set; }

    public bool IsDirty => State is LineState.Added or LineState.Modified or LineState.Deleted;

    public bool IsNew => State == LineState.Added;

    public bool IsModified => State == LineState.Modified;

    public bool IsDeleted => State == LineState.Deleted;

    [ObservableProperty]
    public partial Guid Uid { get; set; }

    [ObservableProperty]
    public partial int Version { get; set; }

    [ObservableProperty]
    public partial DateTime? CreatedDate { get; set; }

    [ObservableProperty]
    public partial string? CreatedBy { get; set; }

    [ObservableProperty]
    public partial DateTime? UpdatedDate { get; set; }

    [ObservableProperty]
    public partial string? UpdatedBy { get; set; }

    internal bool PersistedIsDeleted { get; set; }

    [ObservableProperty]
    public partial bool IsChecked { get; set; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    [ObservableProperty]
    public partial bool IsReadOnly { get; set; }

    [ObservableProperty]
    public partial int RowIndex { get; internal set; }

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
            foreach (var property in GetTrackableProperties())
            {
                if (_baselineValues.TryGetValue(property.Name, out var baseline))
                {
                    property.SetValue(this, baseline);
                }
            }

            _stateBeforeDelete = null;
            State = LineState.Unchanged;
            return;
        }

        _stateBeforeDelete = null;
        State = LineState.Added;
    }

    /// <summary>
    /// 临时抑制程序回填产生的脏状态。最外层作用域结束时，仅将作用域内变更的属性接纳到基线。
    /// </summary>
    public IDisposable SuppressChangeTracking()
    {
        _changeTrackingSuppressionCount++;
        return new ChangeTrackingSuppression(this, SuppressScope());
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
            or nameof(Uid)
            or nameof(Version)
            or nameof(CreatedDate)
            or nameof(CreatedBy)
            or nameof(UpdatedDate)
            or nameof(UpdatedBy)
            or nameof(PersistedIsDeleted)
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

        if (IsSuppressed)
        {
            _changingProperties.Remove(e.PropertyName);
            if (_changeTrackingSuppressionCount > 0 && !IsTrackingIgnoredProperty(e.PropertyName))
            {
                _suppressedChangedProperties.Add(e.PropertyName);
            }

            return;
        }

        if (!_changingProperties.Remove(e.PropertyName) || State == LineState.Deleted)
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

    private void EndChangeTrackingSuppression(IDisposable suppressionScope)
    {
        suppressionScope.Dispose();
        if (_changeTrackingSuppressionCount <= 0 || --_changeTrackingSuppressionCount > 0)
        {
            return;
        }

        if (_suppressedChangedProperties.Count == 0)
        {
            return;
        }

        var properties = GetTrackableProperties()
            .Where(property => _suppressedChangedProperties.Contains(property.Name));
        foreach (var property in properties)
        {
            _baselineValues[property.Name] = property.GetValue(this);
        }

        _suppressedChangedProperties.Clear();
        ReconcileModifiedState();
    }

    private sealed class ChangeTrackingSuppression(
        BaseLineModel<TDto> owner,
        IDisposable suppressionScope) : IDisposable
    {
        private BaseLineModel<TDto>? _owner = owner;
        private IDisposable? _suppressionScope = suppressionScope;

        public void Dispose()
        {
            var currentOwner = Interlocked.Exchange(ref _owner, null);
            var currentScope = Interlocked.Exchange(ref _suppressionScope, null);
            if (currentOwner is not null && currentScope is not null)
            {
                currentOwner.EndChangeTrackingSuppression(currentScope);
            }
        }
    }
}
