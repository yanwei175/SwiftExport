using System.IO;

namespace SwiftExportClient.Services.Files.Excel;

/// <summary>从 XLSX 文件流读取工作表、平表或地址布局快照，不访问业务模型、API 或 Excel 宿主。</summary>
public interface IExcelReadService
{
    /// <summary>读取工作表名称快照；从源流当前位置读取至结尾，源流由调用方释放。</summary>
    Task<ExcelWorkbookInfo> InspectAsync(
        Stream source,
        FileReadLimits? limits = null,
        IProgress<FileOperationProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>按显式工作表及平表范围读取缓存值；保留原始行列位置，不重新计算公式。</summary>
    Task<TableReadResult> ReadTableAsync(
        Stream source,
        string worksheetName,
        ExcelTableReadOptions? options = null,
        IProgress<FileOperationProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>按固定 A1 字段和统一明细边界读取快照；字段键仅用于关联结果，不自动给模型赋值。</summary>
    Task<ExcelLayoutReadResult> ReadLayoutAsync(
        Stream source,
        string worksheetName,
        ExcelLayoutDefinition layout,
        IProgress<FileOperationProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
