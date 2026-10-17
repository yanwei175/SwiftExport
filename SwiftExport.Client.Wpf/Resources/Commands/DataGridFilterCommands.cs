using System.Windows.Controls;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using DataGridExtensions;

namespace SwiftExportClient.Resources.Commands;

/// <summary>提供纯 UI 层的列筛选操作，不访问页面 ViewModel 或业务集合。</summary>
public static class DataGridFilterCommands
{
    /// <summary>提交原生编辑后清除列筛选；输入无效时保留条件和编辑缓冲。</summary>
    public static ICommand Clear { get; } = new RelayCommand<DataGrid>(ClearFilters, grid => grid is { IsEnabled: true });

    private static void ClearFilters(DataGrid? grid)
    {
        if (grid is not { IsEnabled: true }) return;
        if (!grid.CommitEdit(DataGridEditingUnit.Cell, true) || !grid.CommitEdit(DataGridEditingUnit.Row, true)) return;
        grid.GetFilter().Clear();
    }
}
