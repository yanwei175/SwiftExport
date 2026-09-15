using SwiftExport.Contracts.DTOs.Shipping.PackingLists;
namespace SwiftExportClient.Features.Shipping.PackingLists.Models;

/// <summary>选源确认后传回详情的单条结果；调用方仅在弹窗确认时接纳。</summary>
public sealed class PackingListSourcePickerSelection
{
    /// <summary>来源读取快照，包含 ImagePath 与来源版本，不作为保存请求。</summary>
    public PackingListSourceCandidateDto Source { get; init; } = new();
    /// <summary>本次明确选择的数量。</summary>
    public decimal Qty { get; init; }
}
