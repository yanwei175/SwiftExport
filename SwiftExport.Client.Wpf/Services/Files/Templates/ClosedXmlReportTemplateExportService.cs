using System.IO;
using ClosedXML.Excel;
using ClosedXML.Report;
using SwiftExportClient.Services.Files.Internal;

namespace SwiftExportClient.Services.Files.Templates;

/// <summary>在后台使用独立 Report 工作簿生成 XLSX，隔离调用间状态并保留由 Excel 打开时计算的公式。</summary>
public sealed class ClosedXmlReportTemplateExportService : IExcelTemplateExportService
{
    /// <summary>先在内部缓冲区生成完整 XLSX，再复制到调用方目标流；同步 Report 阶段只能在阶段边界合作取消。</summary>
    public Task ExportAsync(
        Stream template,
        Stream target,
        ExcelTemplateData data,
        IProgress<FileOperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(data);
        return Task.Run(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!target.CanWrite || ReferenceEquals(template, target))
                throw new FileOperationException("InvalidOutputStream", "模板流与输出流须分别提供，输出流须可写。");
            using var content = await GenerateAsync(template, data, progress, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                progress?.Report(new FileOperationProgress("WritingOutput"));
                await content.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new FileOperationException("OutputWriteFailed", "Excel 输出流写入失败。", exception);
            }
            AtomicTemplateFileWriter.ReportCompleted(progress);
        });
    }

    /// <summary>在后台生成完整文件，使用同目录临时文件发布；调用方拥有模板流，覆盖必须显式开启。</summary>
    public Task ExportToFileAsync(
        Stream template,
        string targetPath,
        ExcelTemplateData data,
        bool overwrite = false,
        IProgress<FileOperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(data);
        return Task.Run(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fullPath = AtomicTemplateFileWriter.ValidateTarget(targetPath, overwrite);
            using var content = await GenerateAsync(template, data, progress, cancellationToken).ConfigureAwait(false);
            await AtomicTemplateFileWriter.WriteAsync(content, fullPath, overwrite, progress, cancellationToken).ConfigureAwait(false);
        });
    }

    /// <summary>读取有界内部副本，校验变量并生成模板；所有内部资源持续存活到同步阶段退出。</summary>
    private static async Task<MemoryStream> GenerateAsync(
        Stream template,
        ExcelTemplateData data,
        IProgress<FileOperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        TemplateDataValidator.Validate(data, cancellationToken);
        progress?.Report(new FileOperationProgress("ReadingTemplate"));
        using var input = await ReadTemplateAsync(template, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        var output = new MemoryStream();
        try
        {
            using var report = new XLTemplate(input);
            ValidateWorkbookSize(report.Workbook, cancellationToken);
            foreach (var variable in data.Variables)
            {
                cancellationToken.ThrowIfCancellationRequested();
                report.AddVariable(variable.Key, variable.Value!);
            }

            progress?.Report(new FileOperationProgress("GeneratingTemplate"));
            cancellationToken.ThrowIfCancellationRequested();
            var result = report.Generate();
            cancellationToken.ThrowIfCancellationRequested();
            if (result.HasErrors)
            {
                var issues = result.ParsingErrors.Take(100).Select(error =>
                {
                    var address = error.Range.RangeAddress.FirstAddress;
                    return new FileIssue("TemplateGenerationError", "模板表达式或列表区域无法生成。",
                        error.Range.Worksheet.Name, address.RowNumber, address.ColumnNumber,
                        address.ToStringRelative(false), Severity: FileIssueSeverity.Error);
                }).ToArray();
                throw new FileOperationException("TemplateGenerationFailed", "Excel 模板生成失败。", issues: issues);
            }

            ValidateWorkbookSize(report.Workbook, cancellationToken);
            progress?.Report(new FileOperationProgress("TemplateGenerated"));
            cancellationToken.ThrowIfCancellationRequested();
            report.Workbook.CalculateMode = XLCalculateMode.Auto;
            report.Workbook.FullCalculationOnLoad = true;
            report.Workbook.ForceFullCalculation = true;
            // 不主动重算、不验证公式结果；受控列表模板使用 &= 延后生成公式，避免解析时读取原生公式值。
            report.SaveAs(output, new SaveOptions { EvaluateFormulasBeforeSaving = false });
            cancellationToken.ThrowIfCancellationRequested();
            output.Position = 0;
            return output;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            output.Dispose();
            throw;
        }
        catch (FileOperationException)
        {
            output.Dispose();
            throw;
        }
        catch (Exception exception)
        {
            output.Dispose();
            throw new FileOperationException("TemplateGenerationFailed", "Excel 模板生成失败。", exception);
        }
    }

    /// <summary>将读取及容器校验失败归一化为文件服务异常，保留原始异常供技术边界诊断。</summary>
    private static async Task<MemoryStream> ReadTemplateAsync(Stream template, CancellationToken cancellationToken)
    {
        MemoryStream? input = null;
        try
        {
            input = await FileInput.ReadBoundedAsync(template, FileReadLimits.Default, cancellationToken).ConfigureAwait(false);
            FileInput.ValidateXlsx(input, FileReadLimits.Default, cancellationToken);
            return input;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            input?.Dispose();
            throw;
        }
        catch (FileOperationException)
        {
            input?.Dispose();
            throw;
        }
        catch (Exception exception)
        {
            input?.Dispose();
            throw new FileOperationException("TemplateReadFailed", "Excel 模板读取失败。", exception);
        }
    }

    /// <summary>检查模板和生成后的工作簿使用范围；枚举缓存元数据，不主动读取公式结果。</summary>
    private static void ValidateWorkbookSize(IXLWorkbook workbook, CancellationToken cancellationToken)
    {
        var limits = FileReadLimits.Default;
        var cellCount = 0;
        foreach (var worksheet in workbook.Worksheets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var cell in worksheet.CellsUsed(XLCellsUsedOptions.AllContents))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (++cellCount > limits.MaxCells || cell.Address.RowNumber > limits.MaxRows || cell.Address.ColumnNumber > limits.MaxColumns)
                    throw new FileOperationException("TemplateSizeLimitExceeded", "Excel 模板使用范围超过允许上限。",
                        issues: [new FileIssue("TemplateSizeLimitExceeded", "Excel 模板使用范围超过允许上限。",
                            worksheet.Name, cell.Address.RowNumber, cell.Address.ColumnNumber,
                            cell.Address.ToStringRelative(false), Severity: FileIssueSeverity.Error)]);
            }
        }
    }
}
