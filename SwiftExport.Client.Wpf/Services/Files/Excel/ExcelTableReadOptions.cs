using System.Globalization;

namespace SwiftExportClient.Services.Files.Excel;

/// <summary>描述 XLSX 平表的表头、明细起点和读取列范围；只有限定内容的空尾列才被自动排除。</summary>
public sealed record ExcelTableReadOptions
{
    /// <summary>一基表头行号。</summary>
    public int HeaderRow { get; init; } = 1;

    /// <summary>一基明细起始行号，必须位于表头之后。</summary>
    public int DataStartRow { get; init; } = 2;

    /// <summary>一基读取起始列。</summary>
    public int FirstColumn { get; init; } = 1;

    /// <summary>一基读取末列；为空时使用最后一个包含内容的列，不包含仅样式的空尾。</summary>
    public int? LastColumn { get; init; }

    /// <summary>本次文件与完整快照的读取上限。</summary>
    public FileReadLimits Limits { get; init; } = FileReadLimits.Default;

    /// <summary>缓存值显示文本的区域性；默认使用固定区域性，不跟随机器设置。</summary>
    public CultureInfo Culture { get; init; } = CultureInfo.InvariantCulture;
}
