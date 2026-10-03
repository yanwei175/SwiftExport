using SwiftExportClient.Features.Shipping.PackingLists.Models;

namespace SwiftExportClient.Features.Shipping.PackingLists.Services;

/// <summary>按客户选择来源需求；已在草稿中的数量用于本地防超选，取消返回空值。</summary>
public interface IPackingListSourcePickerService
{
    /// <summary>为待关联行选择数量，取消不改变草稿，失败后的上次输入可恢复。</summary>
    Task<IReadOnlyList<PackingListSourcePickerSelection>?> AllocateAsync(Guid customerId, IReadOnlyDictionary<Guid, decimal> draftQuantities,
        Guid? productId, bool nonStandard, string? invoices, decimal limit, IReadOnlyList<PackingListSourcePickerSelection>? previous, CancellationToken token);
    /// <summary>打开独立选源会话，不预占交付量或库存。</summary>
    Task<IReadOnlyList<PackingListSourcePickerSelection>?> PickAsync(Guid customerId,
        IReadOnlyDictionary<Guid, decimal> draftQuantities, CancellationToken token);
}
