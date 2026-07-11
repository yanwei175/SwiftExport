using System.Globalization;

namespace SwiftExport.Wpf.EnterpriseGrid.Exporting;

/// <summary>导出行范围。</summary>
public enum GridExportRowScope
{
    /// <summary>按当前筛选、排序和分组后的视图顺序导出。</summary>
    CurrentView,
    /// <summary>绕过当前视图筛选，导出 ItemsSource 的全部数据项。</summary>
    AllItems,
    /// <summary>按当前视图顺序导出选中数据项。</summary>
    SelectedItems
}

/// <summary>导出值模式。</summary>
public enum GridExportValueMode
{
    /// <summary>使用原始字段值。</summary>
    Raw,
    /// <summary>应用 Lookup、Enum、Converter 和格式得到显示值。</summary>
    Display
}

/// <summary>无法自动读取列值时的处理方式。</summary>
public enum GridUnboundColumnBehavior
{
    /// <summary>跳过无法自动读取的列。</summary>
    Skip,
    /// <summary>遇到无法自动读取的列时抛出异常。</summary>
    Throw
}

/// <summary>文本公式注入防护策略。</summary>
public enum GridFormulaInjectionPolicy
{
    /// <summary>对疑似公式的文本加前导单引号。</summary>
    EscapeText,
    /// <summary>保持文本原样；仅在可信数据场景使用。</summary>
    PreserveText
}

/// <summary>创建导出快照的选项。</summary>
public sealed record GridExportOptions
{
    /// <summary>获取要导出的行范围。</summary>
    public GridExportRowScope RowScope { get; init; } = GridExportRowScope.CurrentView;

    /// <summary>获取原始值或显示值模式。</summary>
    public GridExportValueMode ValueMode { get; init; } = GridExportValueMode.Display;

    /// <summary>获取是否包含隐藏列。</summary>
    public bool IncludeHiddenColumns { get; init; }

    /// <summary>获取是否输出标题行。</summary>
    public bool IncludeHeaders { get; init; } = true;

    /// <summary>获取无绑定列的处理方式。</summary>
    public GridUnboundColumnBehavior UnboundColumnBehavior { get; init; } = GridUnboundColumnBehavior.Skip;

    /// <summary>获取文本公式注入防护策略。</summary>
    public GridFormulaInjectionPolicy FormulaInjectionPolicy { get; init; } = GridFormulaInjectionPolicy.EscapeText;

    /// <summary>获取格式化数值与日期所用区域性。</summary>
    public CultureInfo Culture { get; init; } = CultureInfo.CurrentCulture;

    /// <summary>获取 XLSX 工作表名称。</summary>
    public string WorksheetName { get; init; } = "Data";
}

/// <summary>不含 WPF 控件引用的导出列描述。</summary>
/// <param name="ColumnKey">稳定列标识。</param>
/// <param name="Header">导出标题。</param>
/// <param name="FieldPath">原始字段路径。</param>
/// <param name="DisplayIndex">捕获时的显示顺序。</param>
/// <param name="Format">可选显示格式。</param>
public sealed record GridExportColumn(
    string ColumnKey,
    string Header,
    string? FieldPath,
    int DisplayIndex,
    string? Format);

/// <summary>稳定导出单元格快照。</summary>
/// <param name="RawValue">原始类型值。</param>
/// <param name="DisplayText">已格式化的显示文本。</param>
public sealed record GridExportCell(object? RawValue, string DisplayText);

/// <summary>稳定导出行快照。</summary>
/// <param name="Cells">按列描述顺序排列的 Cell。</param>
public sealed record GridExportRow(IReadOnlyList<GridExportCell> Cells);

/// <summary>与 UI 控件树解耦的完整导出请求。</summary>
/// <param name="Columns">列快照。</param>
/// <param name="Rows">值快照。</param>
/// <param name="Options">请求选项。</param>
public sealed record GridExportRequest(
    IReadOnlyList<GridExportColumn> Columns,
    IReadOnlyList<GridExportRow> Rows,
    GridExportOptions Options);

/// <summary>导出进度。</summary>
/// <param name="CompletedRows">已完成数据行数。</param>
/// <param name="TotalRows">总数据行数。</param>
public sealed record GridExportProgress(int CompletedRows, int TotalRows);

/// <summary>显式列值访问上下文。</summary>
/// <param name="ColumnKey">稳定列标识。</param>
/// <param name="FieldPath">可选字段路径。</param>
/// <param name="ValueMode">请求的值模式。</param>
/// <param name="Culture">格式化区域性。</param>
public sealed record GridExportValueContext(
    string ColumnKey,
    string? FieldPath,
    GridExportValueMode ValueMode,
    CultureInfo Culture);
