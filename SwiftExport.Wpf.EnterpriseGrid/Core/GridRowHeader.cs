using System.Windows;
using System.Windows.Controls;

namespace SwiftExport.Wpf.EnterpriseGrid.Core;

/// <summary>DataGrid RowHeader 的通用呈现状态。</summary>
public static class GridRowHeader
{
    /// <summary>在最前方只读列显示行号，默认关闭；启用时优先于 RowHeader 行号。</summary>
    public static readonly DependencyProperty ShowRowNumberInColumnProperty = DependencyProperty.RegisterAttached(
        "ShowRowNumberInColumn", typeof(bool), typeof(GridRowHeader),
        new PropertyMetadata(false, OnShowRowNumberChanged));

    /// <summary>获取是否使用独立行号列。</summary>
    public static bool GetShowRowNumberInColumn(DependencyObject element) =>
        (bool)element.GetValue(ShowRowNumberInColumnProperty);

    /// <summary>设置是否使用独立行号列，不修改宿主 RowHeader 样式。</summary>
    public static void SetShowRowNumberInColumn(DependencyObject element, bool value) =>
        element.SetValue(ShowRowNumberInColumnProperty, value);
    /// <summary>控制 RowHeader 是否显示当前 DataGrid 视图的一基行号。</summary>
    public static readonly DependencyProperty ShowRowNumberProperty = DependencyProperty.RegisterAttached(
        "ShowRowNumber",
        typeof(bool),
        typeof(GridRowHeader),
        new PropertyMetadata(false, OnShowRowNumberChanged));

    private static readonly DependencyPropertyKey RowNumberPropertyKey = DependencyProperty.RegisterAttachedReadOnly(
        "RowNumber",
        typeof(int),
        typeof(GridRowHeader),
        new PropertyMetadata(0));

    /// <summary>DataGridRow 当前视图中的一基行号；0 表示未启用或未生成。</summary>
    public static readonly DependencyProperty RowNumberProperty = RowNumberPropertyKey.DependencyProperty;

    private static readonly DependencyProperty OriginalHeaderProperty = DependencyProperty.RegisterAttached(
        "OriginalHeader",
        typeof(object),
        typeof(GridRowHeader));

    private static readonly DependencyProperty OwnsHeaderProperty = DependencyProperty.RegisterAttached(
        "OwnsHeader",
        typeof(bool),
        typeof(GridRowHeader),
        new PropertyMetadata(false));

    /// <summary>获取是否显示 DataGrid 行号。</summary>
    public static bool GetShowRowNumber(DependencyObject element) =>
        (bool)element.GetValue(ShowRowNumberProperty);

    /// <summary>设置是否显示 DataGrid 行号。</summary>
    public static void SetShowRowNumber(DependencyObject element, bool value) =>
        element.SetValue(ShowRowNumberProperty, value);

    /// <summary>获取 DataGridRow 当前视图中的一基行号。</summary>
    public static int GetRowNumber(DependencyObject element) =>
        (int)element.GetValue(RowNumberProperty);

    internal static void SetRowNumber(DataGridRow row, int value, bool showInHeader = true)
    {
        if (!showInHeader)
        {
            ClearRowNumber(row);
            row.SetValue(RowNumberPropertyKey, value);
            return;
        }
        row.SetValue(RowNumberPropertyKey, value);
        if (!(bool)row.GetValue(OwnsHeaderProperty))
        {
            row.SetValue(OriginalHeaderProperty, row.Header);
            row.SetValue(OwnsHeaderProperty, true);
        }

        // RowHeader 的模板树无法稳定向上查找 DataGridRow，Header/Content 是原生可靠通道。
        row.Header = value;
    }

    internal static void ClearRowNumber(DataGridRow row)
    {
        row.ClearValue(RowNumberPropertyKey);
        if ((bool)row.GetValue(OwnsHeaderProperty))
        {
            row.Header = row.GetValue(OriginalHeaderProperty);
            row.ClearValue(OriginalHeaderProperty);
            row.ClearValue(OwnsHeaderProperty);
        }
    }

    private static void OnShowRowNumberChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is DataGrid dataGrid)
        {
            EnterpriseGrid.GetCoordinator(dataGrid)?.RowNumbers.RefreshEnabledState();
        }
    }
}
