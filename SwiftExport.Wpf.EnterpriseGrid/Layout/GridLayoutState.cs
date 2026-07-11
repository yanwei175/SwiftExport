using System.ComponentModel;

namespace SwiftExport.Wpf.EnterpriseGrid.Layout;

/// <summary>可序列化友好的列宽单位。</summary>
public enum GridColumnWidthUnit
{
    /// <summary>自动宽度。</summary>
    Auto,
    /// <summary>固定像素宽度。</summary>
    Pixel,
    /// <summary>按 Cell 内容计算。</summary>
    SizeToCells,
    /// <summary>按 Header 内容计算。</summary>
    SizeToHeader,
    /// <summary>星号比例宽度。</summary>
    Star
}

/// <summary>列宽快照。</summary>
/// <param name="Value">宽度值。</param>
/// <param name="Unit">宽度单位。</param>
public sealed record GridColumnWidth(double Value, GridColumnWidthUnit Unit);

/// <summary>单列布局快照。</summary>
/// <param name="ColumnKey">稳定列标识。</param>
/// <param name="IsVisible">是否可见。</param>
/// <param name="DisplayIndex">显示顺序。</param>
/// <param name="Width">列宽。</param>
/// <param name="SortDirection">排序方向。</param>
/// <param name="SortIndex">多列排序优先级。</param>
public sealed record GridColumnLayout(
    string ColumnKey,
    bool IsVisible,
    int DisplayIndex,
    GridColumnWidth Width,
    ListSortDirection? SortDirection = null,
    int? SortIndex = null);

/// <summary>完整 Grid 布局快照；未知列 Key 在恢复时被忽略。</summary>
public sealed record GridLayoutState
{
    /// <summary>创建布局状态快照。</summary>
    /// <param name="version">布局版本。</param>
    /// <param name="columns">列布局。</param>
    /// <param name="frozenColumnCount">冻结列数。</param>
    public GridLayoutState(int version, IEnumerable<GridColumnLayout>? columns, int frozenColumnCount = 0)
    {
        Version = version;
        Columns = columns?.ToArray() ?? Array.Empty<GridColumnLayout>();
        FrozenColumnCount = Math.Max(0, frozenColumnCount);
    }

    /// <summary>获取布局版本。</summary>
    public int Version { get; }

    /// <summary>获取列布局快照。</summary>
    public IReadOnlyList<GridColumnLayout> Columns { get; }

    /// <summary>获取冻结列数。</summary>
    public int FrozenColumnCount { get; }
}

/// <summary>布局恢复结果。</summary>
/// <param name="Succeeded">是否成功。</param>
/// <param name="Error">失败文本。</param>
/// <param name="IgnoredColumnKeys">被忽略的未知列 Key。</param>
public sealed record GridLayoutApplyResult(
    bool Succeeded,
    string? Error,
    IReadOnlyList<string> IgnoredColumnKeys)
{
    /// <summary>创建成功结果。</summary>
    public static GridLayoutApplyResult Success(IReadOnlyList<string>? ignoredColumnKeys = null) =>
        new(true, null, ignoredColumnKeys ?? Array.Empty<string>());

    /// <summary>创建失败结果。</summary>
    public static GridLayoutApplyResult Failure(string error) =>
        new(false, error, Array.Empty<string>());
}
