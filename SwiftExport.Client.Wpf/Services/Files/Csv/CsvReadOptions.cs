using System.Text;

namespace SwiftExportClient.Services.Files.Csv;

/// <summary>
/// 指定 CSV 的编码、分隔符、表头语义和读取上限；不根据系统文化或内容猜测格式。
/// </summary>
public sealed record CsvReadOptions
{
    /// <summary>
    /// 输入编码，默认严格 UTF-8；仅识别所选编码自身的 BOM，坏字节始终拒绝。
    /// </summary>
    public Encoding Encoding { get; init; } = new UTF8Encoding(false, true);

    /// <summary>
    /// 字段分隔符，默认逗号；支持显式指定制表符或其它合法分隔符。
    /// </summary>
    public string Delimiter { get; init; } = ",";

    /// <summary>
    /// 是否将第一条逻辑记录作为表头；为 false 时所有记录都进入数据行，表头集合为空。
    /// </summary>
    public bool HasHeaderRecord { get; init; } = true;

    /// <summary>
    /// 文件和结果的容量上限；行数只计数据行，单元格数包含表头。
    /// </summary>
    public FileReadLimits Limits { get; init; } = FileReadLimits.Default;
}
