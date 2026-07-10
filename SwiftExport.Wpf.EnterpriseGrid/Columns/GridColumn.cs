using System.Windows;
using System.Windows.Controls;

namespace SwiftExport.Wpf.EnterpriseGrid.Columns;

/// <summary>
/// 为任意原生 DataGridColumn 提供跨模块共享的稳定元数据。
/// </summary>
public static class GridColumn
{
    /// <summary>
    /// 列的稳定标识。需要双向 CurrentCell、筛选、导出或布局持久化时必须设置。
    /// </summary>
    public static readonly DependencyProperty KeyProperty = DependencyProperty.RegisterAttached(
        "Key",
        typeof(string),
        typeof(GridColumn),
        new PropertyMetadata(null),
        IsValidKey);

    /// <summary>
    /// 标识该列是否参与 Enterprise Grid 的键盘单元格导航。默认参与。
    /// </summary>
    public static readonly DependencyProperty CanNavigateProperty = DependencyProperty.RegisterAttached(
        "CanNavigate",
        typeof(bool),
        typeof(GridColumn),
        new PropertyMetadata(true));

    /// <summary>
    /// 数据字段路径。验证、筛选与导出需要稳定字段语义时设置；原生绑定列可自动推断简单 Path。
    /// </summary>
    public static readonly DependencyProperty FieldPathProperty = DependencyProperty.RegisterAttached(
        "FieldPath",
        typeof(string),
        typeof(GridColumn),
        new PropertyMetadata(null));

    /// <summary>
    /// 获取列的稳定标识。
    /// </summary>
    public static string? GetKey(DependencyObject element)
    {
        return (string?)element.GetValue(KeyProperty);
    }

    /// <summary>
    /// 设置列的稳定标识。
    /// </summary>
    public static void SetKey(DependencyObject element, string? value)
    {
        element.SetValue(KeyProperty, value?.Trim());
    }

    /// <summary>获取该列是否参与键盘单元格导航。</summary>
    public static bool GetCanNavigate(DependencyObject element) =>
        (bool)element.GetValue(CanNavigateProperty);

    /// <summary>设置该列是否参与键盘单元格导航。</summary>
    public static void SetCanNavigate(DependencyObject element, bool value) =>
        element.SetValue(CanNavigateProperty, value);

    /// <summary>获取列对应的数据字段路径。</summary>
    public static string? GetFieldPath(DependencyObject element) =>
        (string?)element.GetValue(FieldPathProperty);

    /// <summary>设置列对应的数据字段路径。</summary>
    public static void SetFieldPath(DependencyObject element, string? value) =>
        element.SetValue(FieldPathProperty, string.IsNullOrWhiteSpace(value) ? null : value.Trim());

    internal static DataGridColumn? FindByKey(DataGrid dataGrid, string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        DataGridColumn? match = null;
        foreach (var column in dataGrid.Columns)
        {
            if (!string.Equals(GetKey(column), key, StringComparison.Ordinal))
            {
                continue;
            }

            if (match is not null)
            {
                throw new InvalidOperationException($"DataGrid 中存在重复的 Enterprise Grid ColumnKey：{key}");
            }

            match = column;
        }

        return match;
    }

    private static bool IsValidKey(object value)
    {
        return value is null || value is string key && !string.IsNullOrWhiteSpace(key);
    }
}
