using System.IO;
using Microsoft.Win32;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Wpf.EnterpriseGrid.Exporting;

namespace SwiftExportClient.Features.DemandPlanning.NetDemand.Services;

public sealed class NetDemandTraceExportService : INetDemandTraceExportService
{
    private readonly IGridExportProvider _provider = new XlsxGridExportProvider();

    public async Task<string?> ExportSourceAsync(
        SourcePurchaseTraceDto trace,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(trace);
        var path = SelectPath(trace.SourceNo);
        if (path is null) return null;
        var request = NetDemandTraceExportBuilder.CreateRequest(trace);
        await using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        await _provider.ExportAsync(request, stream, cancellationToken: cancellationToken);
        return path;
    }

    public async Task<string?> ExportAsync(
        NetDemandListItemDto summary,
        IReadOnlyList<NetDemandAllocationDto> allocations,
        IReadOnlyList<NetDemandExecutionLinkDto> executionLinks,
        CancellationToken cancellationToken = default)
    {
        var path = SelectPath(summary.SourceNo);
        if (path is null) return null;
        var request = NetDemandTraceExportBuilder.CreateRequest(summary, allocations, executionLinks);
        await using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        await _provider.ExportAsync(request, stream, cancellationToken: cancellationToken);
        return path;
    }

    private static string? SelectPath(string? sourceNo)
    {
        var dialog = new SaveFileDialog
        {
            Title = "导出净需求采购追溯",
            Filter = "Excel 工作簿 (*.xlsx)|*.xlsx",
            DefaultExt = ".xlsx",
            AddExtension = true,
            FileName = $"净需求采购追溯_{SanitizeFileName(sourceNo)}_{DateTime.Now:yyyyMMddHHmm}.xlsx",
        };
        if (dialog.ShowDialog() != true)
        {
            return null;
        }
        return dialog.FileName;
    }

    private static string SanitizeFileName(string? value)
    {
        var text = string.IsNullOrWhiteSpace(value) ? "未命名来源" : value.Trim();
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            text = text.Replace(invalid, '_');
        }

        return text;
    }
}
