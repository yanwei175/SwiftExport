using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Data;

namespace SwiftExport.Wpf.EnterpriseGrid.Filtering.Internal;

internal sealed class GridFilterModule
{
    private readonly DataGrid _dataGrid;
    private readonly DependencyPropertyDescriptor _itemsSourceDescriptor;
    private readonly List<GroupDescription> _ownedGroups = [];
    private ICollectionView? _localView;
    private Predicate<object>? _originalFilter;
    private Predicate<object>? _ownedFilter;
    private bool _isAttached;

    public GridFilterModule(DataGrid dataGrid)
    {
        _dataGrid = dataGrid;
        _itemsSourceDescriptor = DependencyPropertyDescriptor.FromProperty(
            ItemsControl.ItemsSourceProperty,
            typeof(DataGrid));
    }

    public void Attach()
    {
        if (_isAttached)
        {
            return;
        }

        _isAttached = true;
        _itemsSourceDescriptor.AddValueChanged(_dataGrid, OnItemsSourceChanged);
        ApplyConfiguration();
    }

    public void Detach()
    {
        if (!_isAttached)
        {
            return;
        }

        _itemsSourceDescriptor.RemoveValueChanged(_dataGrid, OnItemsSourceChanged);
        RestoreLocalView();
        _isAttached = false;
    }

    internal void ApplyConfiguration()
    {
        if (!_isAttached)
        {
            return;
        }

        var mode = GridFilter.GetMode(_dataGrid);
        if (mode == GridFilterMode.Disabled)
        {
            RestoreLocalView();
            GridFilter.SetApplyResult(_dataGrid, true, null);
            return;
        }

        if (!TryValidateConfiguration(out var configurationError))
        {
            GridFilter.SetApplyResult(_dataGrid, false, configurationError);
            return;
        }

        if (!CompleteEditTransaction())
        {
            GridFilter.SetApplyResult(_dataGrid, false, "当前编辑未通过验证，筛选未应用。");
            return;
        }

        if (mode == GridFilterMode.Local)
        {
            if (!ApplyLocal(out var localError))
            {
                GridFilter.SetApplyResult(_dataGrid, false, localError);
                return;
            }
        }
        else
        {
            RestoreLocalView();
            if (mode == GridFilterMode.Remote)
            {
                if (!ExecuteRemote(out var remoteError))
                {
                    GridFilter.SetApplyResult(_dataGrid, false, remoteError);
                    return;
                }
            }
        }

        GridFilter.SetApplyResult(_dataGrid, true, null);
    }

    private bool ApplyLocal(out string? error)
    {
        var view = CollectionViewSource.GetDefaultView(_dataGrid.ItemsSource ?? _dataGrid.Items);
        if (!ReferenceEquals(view, _localView))
        {
            RestoreLocalView();
            _localView = view;
            _originalFilter = view.Filter;
        }
        else if (!ReferenceEquals(view.Filter, _ownedFilter))
        {
            _originalFilter = view.Filter;
        }

        var filterSet = GridFilter.GetFilterSet(_dataGrid);
        if (filterSet.Descriptors.Count > 0 && !view.CanFilter)
        {
            error = "当前 ICollectionView 不支持本地筛选。";
            return false;
        }

        if (GridFilter.GetGroups(_dataGrid).Count > 0 && !view.CanGroup)
        {
            error = "当前 ICollectionView 不支持本地分组。";
            return false;
        }

        _ownedFilter = item => (_originalFilter?.Invoke(item) ?? true) && GridFilterPredicate.Matches(item, filterSet);
        if (view.CanFilter)
        {
            view.Filter = _ownedFilter;
        }

        ApplyGroups(view);
        view.Refresh();
        error = null;
        return true;
    }

    private void ApplyGroups(ICollectionView view)
    {
        foreach (var group in _ownedGroups)
        {
            view.GroupDescriptions?.Remove(group);
        }

        _ownedGroups.Clear();
        if (!view.CanGroup || view.GroupDescriptions is null)
        {
            return;
        }

        foreach (var descriptor in GridFilter.GetGroups(_dataGrid))
        {
            var group = new PropertyGroupDescription(descriptor.FieldPath);
            _ownedGroups.Add(group);
            view.GroupDescriptions.Add(group);
        }
    }

    private bool ExecuteRemote(out string? error)
    {
        var command = GridFilter.GetRemoteFilterCommand(_dataGrid);
        var request = new GridFilterRequest(GridFilter.GetFilterSet(_dataGrid), GridFilter.GetGroups(_dataGrid));
        if (command is null)
        {
            error = "Remote 筛选模式需要显式绑定 RemoteFilterCommand。";
            return false;
        }

        if (!command.CanExecute(request))
        {
            error = "RemoteFilterCommand 当前不可执行。";
            return false;
        }

        command.Execute(request);
        error = null;
        return true;
    }

    private void RestoreLocalView()
    {
        if (_localView is null)
        {
            return;
        }

        foreach (var group in _ownedGroups)
        {
            _localView.GroupDescriptions?.Remove(group);
        }

        _ownedGroups.Clear();
        if (ReferenceEquals(_localView.Filter, _ownedFilter))
        {
            _localView.Filter = _originalFilter;
        }

        _localView.Refresh();
        _localView = null;
        _originalFilter = null;
        _ownedFilter = null;
    }

    private bool CompleteEditTransaction()
    {
        return GridFilter.GetEditCommitMode(_dataGrid) switch
        {
            GridFilterEditCommitMode.CancelEdit => CancelEditTransaction(),
            GridFilterEditCommitMode.CommitCell => _dataGrid.CommitEdit(DataGridEditingUnit.Cell, true),
            _ => _dataGrid.CommitEdit(DataGridEditingUnit.Cell, true) &&
                 _dataGrid.CommitEdit(DataGridEditingUnit.Row, true)
        };
    }

    private bool TryValidateConfiguration(out string? error)
    {
        if (GridFilter.GetFilterSet(_dataGrid).Descriptors.Any(descriptor =>
                string.IsNullOrWhiteSpace(descriptor.ColumnKey) || string.IsNullOrWhiteSpace(descriptor.FieldPath)))
        {
            error = "筛选描述必须包含非空 ColumnKey 与 FieldPath。";
            return false;
        }

        if (GridFilter.GetGroups(_dataGrid).Any(descriptor =>
                string.IsNullOrWhiteSpace(descriptor.ColumnKey) || string.IsNullOrWhiteSpace(descriptor.FieldPath)))
        {
            error = "分组描述必须包含非空 ColumnKey 与 FieldPath。";
            return false;
        }

        error = null;
        return true;
    }

    private bool CancelEditTransaction()
    {
        _dataGrid.CancelEdit(DataGridEditingUnit.Cell);
        _dataGrid.CancelEdit(DataGridEditingUnit.Row);
        return true;
    }

    private void OnItemsSourceChanged(object? sender, EventArgs e)
    {
        RestoreLocalView();
        ApplyConfiguration();
    }
}
