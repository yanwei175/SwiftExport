using System.IO;

namespace SwiftExportClient.Services.Files.Csv;

/// <summary>
/// 将调用方提供的 CSV 流读取为有界纯数据快照，不猜测字段类型，也不参与业务模型赋值。
/// </summary>
public interface ICsvReadService
{
    /// <summary>
    /// 从流的当前位置读到结尾；保留原始字段文本和来源位置，不关闭调用方的流。
    /// </summary>
    Task<TableReadResult> ReadTableAsync(
        Stream source,
        CsvReadOptions? options = null,
        IProgress<FileOperationProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
