using System.Globalization;

namespace SwiftExportClient.Services.Files.Excel;

/// <summary>固定单头和向下明细的纯读取布局，不依赖服务端配置 DTO 或业务字段目录。</summary>
public sealed record ExcelLayoutDefinition
{
    /// <summary>本次读取字段；所有向下字段必须从同一行开始。</summary>
    public IReadOnlyList<ExcelLayoutField> Fields { get; init; } = [];

    /// <summary>有向下明细字段时采用的统一结束边界。</summary>
    public ExcelLayoutEndMode EndMode { get; init; } = ExcelLayoutEndMode.LastNonEmptyColumn;

    /// <summary>末行判断模式的列字母，如 A；不接受范围或单格地址。</summary>
    public string? LastRowColumn { get; init; }

    /// <summary>汇总边界模式的单个 A1 地址，该行必须位于明细起始行之后。</summary>
    public string? SummaryCellAddress { get; init; }

    /// <summary>本次文件与完整快照的读取上限。</summary>
    public FileReadLimits Limits { get; init; } = FileReadLimits.Default;

    /// <summary>缓存值显示文本的区域性；默认使用固定区域性。</summary>
    public CultureInfo Culture { get; init; } = CultureInfo.InvariantCulture;
}
