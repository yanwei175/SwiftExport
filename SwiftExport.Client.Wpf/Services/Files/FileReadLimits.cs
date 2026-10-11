namespace SwiftExportClient.Services.Files;

/// <summary>限制输入字节、解压内容和读取快照规模，避免将批量导入当作无限量工作簿处理。</summary>
public sealed record FileReadLimits
{
    /// <summary>提供适用于几百到几千行导入的默认保护值。</summary>
    public static FileReadLimits Default { get; } = new();

    /// <summary>允许从输入流读取的最大字节数，默认 20 MiB。</summary>
    public long MaxInputBytes { get; init; } = 20 * 1024 * 1024;

    /// <summary>XLSX 包各条目声明的解压总字节上限，默认 100 MiB。</summary>
    public long MaxExpandedBytes { get; init; } = 100 * 1024 * 1024;

    /// <summary>读取结果允许的最大明细行数。</summary>
    public int MaxRows { get; init; } = 10_000;

    /// <summary>读取结果允许的最大列数或布局字段数。</summary>
    public int MaxColumns { get; init; } = 256;

    /// <summary>读取快照的最大单元格数，包含表头或单头。</summary>
    public int MaxCells { get; init; } = 250_000;

    /// <summary>在创建内部缓冲或工作簿前检查调用方设置的边界。</summary>
    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxInputBytes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(MaxInputBytes, int.MaxValue);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxExpandedBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxRows);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxColumns);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxCells);
    }
}
