using SwiftExport.Contracts.DTOs.Inventory.Inbounds.Sources;
using SwiftExportClient.Features.Inventory.Inbounds.Models;

namespace SwiftExportClient.Features.Inventory.Inbounds.Services;

/// <summary>固定单据类型的来源选择会话，取消返回空值。</summary>
public interface IInboundSourceDialogService
{
    /// <summary>只在确认时返回独立来源快照，不复核版本或修改采购事实。</summary>
    Task<IReadOnlyList<InboundSourceCandidateDto>?> PickAsync(InboundSourcePickerRequest request, CancellationToken cancellationToken = default);
}
