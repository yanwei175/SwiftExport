using System.Windows;
using System.Windows.Controls;
using SwiftExport.Wpf.EnterpriseGrid.Editing.Internal;
using SwiftExport.Wpf.EnterpriseGrid.Filtering.Internal;
using SwiftExport.Wpf.EnterpriseGrid.Selection.Internal;
using SwiftExport.Wpf.EnterpriseGrid.Validation.Internal;

namespace SwiftExport.Wpf.EnterpriseGrid.Core.Internal;

/// <summary>
/// 统一管理单个 DataGrid 的模块生命周期，避免多个模块重复争抢输入事件。
/// </summary>
internal sealed class GridCoordinator : IDisposable
{
    private readonly DataGrid _dataGrid;
    private bool _isAttached;

    public GridCoordinator(DataGrid dataGrid)
    {
        _dataGrid = dataGrid;
        Selection = new GridSelectionModule(dataGrid);
        Editing = new GridEditingModule(dataGrid);
        Validation = new GridValidationModule(dataGrid);
        Filtering = new GridFilterModule(dataGrid);
    }

    internal GridSelectionModule Selection { get; }

    internal GridEditingModule Editing { get; }

    internal GridValidationModule Validation { get; }

    internal GridFilterModule Filtering { get; }

    internal bool IsActive { get; private set; }

    public void Attach()
    {
        if (_isAttached)
        {
            return;
        }

        _isAttached = true;
        _dataGrid.Loaded += OnLoaded;
        _dataGrid.Unloaded += OnUnloaded;

        if (_dataGrid.IsLoaded)
        {
            Activate();
        }
    }

    public void Dispose()
    {
        if (!_isAttached)
        {
            return;
        }

        _dataGrid.Loaded -= OnLoaded;
        _dataGrid.Unloaded -= OnUnloaded;
        Deactivate();
        _isAttached = false;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Activate();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Deactivate();
    }

    private void Activate()
    {
        if (IsActive)
        {
            return;
        }

        Selection.Attach();
        Validation.Attach();
        Filtering.Attach();
        Editing.Attach();
        IsActive = true;
    }

    private void Deactivate()
    {
        if (!IsActive)
        {
            return;
        }

        Editing.Detach();
        Filtering.Detach();
        Validation.Detach();
        Selection.Detach();
        IsActive = false;
    }
}
