using SwiftExport.Contracts.DTOs.Inventory.Outbounds;
using SwiftExportClient.Features.Inventory.Outbounds.Models;

namespace SwiftExportClient.Features.Inventory.Outbounds.Services;

/// <summary>固定单据类型的来源选择会话，取消返回空值。</summary>
public interface IOutboundItemDialogService
{
    /// <summary>只在确认时返回库存候选快照；不预占库存，过账时重新校验余额。</summary>
    Task<IReadOnlyList<OutboundItemCandidateDto>?> PickAsync(OutboundItemPickerRequest request, CancellationToken cancellationToken = default);
}
