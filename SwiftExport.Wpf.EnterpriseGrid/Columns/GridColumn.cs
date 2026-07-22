using System.Windows;
using System.Windows.Controls;
using SwiftExport.Wpf.EnterpriseGrid.Exporting;

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
    /// Ctrl+D 向下填充时作为一个逻辑单元格原子复制的字段路径集合。
    /// 多个路径使用逗号、分号或竖线分隔；为空时回退到 FieldPath 或原生 Binding.Path。
    /// </summary>
    public static readonly DependencyProperty FillDownFieldPathsProperty = DependencyProperty.RegisterAttached(
        "FillDownFieldPaths",
        typeof(string),
        typeof(GridColumn),
        new PropertyMetadata(null));

    /// <summary>标识列是否参与筛选。默认参与。</summary>
    public static readonly DependencyProperty CanFilterProperty = DependencyProperty.RegisterAttached(
        "CanFilter",
        typeof(bool),
        typeof(GridColumn),
        new PropertyMetadata(true));

    /// <summary>标识列是否参与导出。默认参与。</summary>
    public static readonly DependencyProperty CanExportProperty = DependencyProperty.RegisterAttached(
        "CanExport",
        typeof(bool),
        typeof(GridColumn),
        new PropertyMetadata(true));

    /// <summary>导出标题；为空时回退到原生 Header 的文本表达。</summary>
    public static readonly DependencyProperty ExportHeaderProperty = DependencyProperty.RegisterAttached(
        "ExportHeader",
        typeof(string),
        typeof(GridColumn),
        new PropertyMetadata(null));

    /// <summary>模板列或特殊显示值的显式导出值访问器。</summary>
    public static readonly DependencyProperty ValueAccessorProperty = DependencyProperty.RegisterAttached(
        "ValueAccessor",
        typeof(IGridValueAccessor),
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

    /// <summary>获取 Ctrl+D 向下填充使用的复合字段路径。</summary>
    public static string? GetFillDownFieldPaths(DependencyObject element) =>
        (string?)element.GetValue(FillDownFieldPathsProperty);

    /// <summary>设置 Ctrl+D 向下填充使用的复合字段路径。</summary>
    public static void SetFillDownFieldPaths(DependencyObject element, string? value) =>
        element.SetValue(
            FillDownFieldPathsProperty,
            string.IsNullOrWhiteSpace(value) ? null : value.Trim());

    /// <summary>获取列是否参与筛选。</summary>
    public static bool GetCanFilter(DependencyObject element) => (bool)element.GetValue(CanFilterProperty);

    /// <summary>设置列是否参与筛选。</summary>
    public static void SetCanFilter(DependencyObject element, bool value) => element.SetValue(CanFilterProperty, value);

    /// <summary>获取列是否参与导出。</summary>
    public static bool GetCanExport(DependencyObject element) => (bool)element.GetValue(CanExportProperty);

    /// <summary>设置列是否参与导出。</summary>
    public static void SetCanExport(DependencyObject element, bool value) => element.SetValue(CanExportProperty, value);

    /// <summary>获取列的导出标题。</summary>
    public static string? GetExportHeader(DependencyObject element) =>
        (string?)element.GetValue(ExportHeaderProperty);

    /// <summary>设置列的导出标题。</summary>
    public static void SetExportHeader(DependencyObject element, string? value) =>
        element.SetValue(ExportHeaderProperty, string.IsNullOrWhiteSpace(value) ? null : value.Trim());

    /// <summary>获取显式导出值访问器。</summary>
    public static IGridValueAccessor? GetValueAccessor(DependencyObject element) =>
        (IGridValueAccessor?)element.GetValue(ValueAccessorProperty);

    /// <summary>设置显式导出值访问器。</summary>
    public static void SetValueAccessor(DependencyObject element, IGridValueAccessor? value) =>
        element.SetValue(ValueAccessorProperty, value);

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
