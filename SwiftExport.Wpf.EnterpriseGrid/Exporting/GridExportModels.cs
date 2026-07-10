using System.Globalization;

namespace SwiftExport.Wpf.EnterpriseGrid.Exporting;

/// <summary>导出行范围。</summary>
public enum GridExportRowScope
{
    CurrentView,
    AllItems,
    SelectedItems
}

/// <summary>导出值模式。</summary>
public enum GridExportValueMode
{
    Raw,
    Display
}

/// <summary>无法自动读取列值时的处理方式。</summary>
public enum GridUnboundColumnBehavior
{
    Skip,
    Throw
}

/// <summary>文本公式注入防护策略。</summary>
public enum GridFormulaInjectionPolicy
{
    EscapeText,
    PreserveText
}

/// <summary>创建导出快照的选项。</summary>
public sealed record GridExportOptions
{
    public GridExportRowScope RowScope { get; init; } = GridExportRowScope.CurrentView;

    public GridExportValueMode ValueMode { get; init; } = GridExportValueMode.Display;

    public bool IncludeHiddenColumns { get; init; }

    public bool IncludeHeaders { get; init; } = true;

    public GridUnboundColumnBehavior UnboundColumnBehavior { get; init; } = GridUnboundColumnBehavior.Skip;

    public GridFormulaInjectionPolicy FormulaInjectionPolicy { get; init; } = GridFormulaInjectionPolicy.EscapeText;

    public CultureInfo Culture { get; init; } = CultureInfo.CurrentCulture;

    public string WorksheetName { get; init; } = "Data";
}

/// <summary>不含 WPF 控件引用的导出列描述。</summary>
public sealed record GridExportColumn(
    string ColumnKey,
    string Header,
    string? FieldPath,
    int DisplayIndex,
    string? Format);

/// <summary>稳定导出单元格快照。</summary>
public sealed record GridExportCell(object? RawValue, string DisplayText);

/// <summary>稳定导出行快照。</summary>
public sealed record GridExportRow(IReadOnlyList<GridExportCell> Cells);

/// <summary>与 UI 控件树解耦的完整导出请求。</summary>
public sealed record GridExportRequest(
    IReadOnlyList<GridExportColumn> Columns,
    IReadOnlyList<GridExportRow> Rows,
    GridExportOptions Options);

/// <summary>导出进度。</summary>
public sealed record GridExportProgress(int CompletedRows, int TotalRows);

/// <summary>显式列值访问上下文。</summary>
public sealed record GridExportValueContext(
    string ColumnKey,
    string? FieldPath,
    GridExportValueMode ValueMode,
    CultureInfo Culture);
