using System.ComponentModel;

namespace SwiftExport.Wpf.EnterpriseGrid.Layout;

/// <summary>可序列化友好的列宽单位。</summary>
public enum GridColumnWidthUnit
{
    Auto,
    Pixel,
    SizeToCells,
    SizeToHeader,
    Star
}

/// <summary>列宽快照。</summary>
public sealed record GridColumnWidth(double Value, GridColumnWidthUnit Unit);

/// <summary>单列布局快照。</summary>
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
    public GridLayoutState(int version, IEnumerable<GridColumnLayout>? columns, int frozenColumnCount = 0)
    {
        Version = version;
        Columns = columns?.ToArray() ?? Array.Empty<GridColumnLayout>();
        FrozenColumnCount = Math.Max(0, frozenColumnCount);
    }

    public int Version { get; }

    public IReadOnlyList<GridColumnLayout> Columns { get; }

    public int FrozenColumnCount { get; }
}

/// <summary>布局恢复结果。</summary>
public sealed record GridLayoutApplyResult(
    bool Succeeded,
    string? Error,
    IReadOnlyList<string> IgnoredColumnKeys)
{
    public static GridLayoutApplyResult Success(IReadOnlyList<string>? ignoredColumnKeys = null) =>
        new(true, null, ignoredColumnKeys ?? Array.Empty<string>());

    public static GridLayoutApplyResult Failure(string error) =>
        new(false, error, Array.Empty<string>());
}
