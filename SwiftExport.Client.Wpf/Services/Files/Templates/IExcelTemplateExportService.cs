using System.IO;

namespace SwiftExportClient.Services.Files.Templates;

/// <summary>使用受控 XLSX 模板生成 Excel；只保留公式，由打开文件的 Excel 完成计算。</summary>
public interface IExcelTemplateExportService
{
    /// <summary>从模板流的当前位置读取并导出到目标流的当前位置；两个流均由调用方释放，复制失败时目标可能含部分内容。</summary>
    Task ExportAsync(
        Stream template,
        Stream target,
        ExcelTemplateData data,
        IProgress<FileOperationProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>生成完成后通过同目录临时文件发布 XLSX；默认拒绝覆盖，失败或发布前取消时保留原文件。</summary>
    Task ExportToFileAsync(
        Stream template,
        string targetPath,
        ExcelTemplateData data,
        bool overwrite = false,
        IProgress<FileOperationProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
