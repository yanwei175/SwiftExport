using System.Windows;
using System.Windows.Controls;
using SwiftExport.Wpf.EnterpriseGrid.Selection.Internal;

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
    }

    internal GridSelectionModule Selection { get; }

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
        IsActive = true;
    }

    private void Deactivate()
    {
        if (!IsActive)
        {
            return;
        }

        Selection.Detach();
        IsActive = false;
    }
}
