using Mapster;
using SwiftExport.Application.Services.Shipping;
using SwiftExport.Contracts.DTOs.Shipping.PackingLists;

namespace SwiftExport.Application.Mappings;

/// <summary>集中定义内部计算结果到只读预览的字段映射，显示关联由服务组装。</summary>
public sealed class PackingListPostingMapping : IRegister
{
    /// <inheritdoc/>
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<PackingListPostingPreviewBuilder.Action, PackingListPostingActionDto>()
            .Map(d => d.ActionKind, s => s.Kind).Map(d => d.Quantity, s => s.Qty)
            .Map(d => d.WarehouseSiteId, s => s.WarehouseId)
            .Map(d => d.RootNetDemandId, s => s.RootId).Map(d => d.ComponentNetDemandId, s => s.ComponentId);
        config.NewConfig<PackingListPostingPreviewBuilder.Blocker, PackingListPostingBlockerDto>()
            .Map(d => d.ShortfallQty, s => s.Shortfall);
    }
}
