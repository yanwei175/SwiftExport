namespace SwiftExportClient.Features.Inventory.Outbounds.Models;
/// <summary>选择库存产品的固定仓库及已选择产品；只属于客户端弹窗协作。</summary>
public sealed class OutboundItemPickerRequest
{
    public Guid WarehouseSiteId { get; init; }
    public IReadOnlyList<Guid> ExcludedProductIds { get; init; } = Array.Empty<Guid>();
}
