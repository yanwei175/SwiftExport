using SwiftExportClient.Features.Shipping.PackingLists.Models;

namespace SwiftExportClient.Features.Shipping.PackingLists.Services;

/// <summary>按客户选择来源需求；已在草稿中的数量用于本地防超选，取消返回空值。</summary>
public interface IPackingListSourcePickerService
{
    /// <summary>打开独立选源会话，不预占交付量或库存。</summary>
    Task<IReadOnlyList<PackingListSourcePickerSelection>?> PickAsync(Guid customerId,
        IReadOnlyDictionary<Guid, decimal> draftQuantities, CancellationToken token);
}
