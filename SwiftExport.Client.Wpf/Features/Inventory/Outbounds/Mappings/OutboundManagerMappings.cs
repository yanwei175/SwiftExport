using Mapster;
using SwiftExport.Contracts.DTOs.Inventory.Outbounds;
using SwiftExportClient.Features.Inventory.Outbounds.Models;

namespace SwiftExportClient.Features.Inventory.Outbounds.Mappings;

/// <summary>集中映射出库详情与编辑模型，排除 UI 状态和头行集合的隐式复制。</summary>
public sealed class OutboundManagerMappings : IRegister
{
    /// <inheritdoc/>
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<OutboundManagerListItemDto, OutboundHeaderDetailDto>();
        config.NewConfig<OutboundLineModel, OutboundDraftLineInput>()
            .Map(x => x.Uid, x => x.IsNew ? Guid.Empty : x.Uid);
        config.NewConfig<OutboundHeaderModel, OutboundDraftData>()
            .Map(x => x.Lines, x => x.Lines.Items);
        config.NewConfig<OutboundItemCandidateDto, OutboundItemPickerRowModel>().Ignore(x => x.IsChecked);
        config.NewConfig<OutboundItemPickerRowModel, OutboundItemCandidateDto>();
        config.NewConfig<OutboundItemCandidateDto, OutboundLineDetailDto>()
            .Map(x => x.SkuSnapshot, x => x.Sku)
            .Map(x => x.DescriptionSnapshot, x => x.Description)
            .Map(x => x.UnitSnapshot, x => x.Unit);
        config.NewConfig<OutboundHeaderDetailDto, OutboundHeaderModel>()
            .IgnoreNullValues(true).Ignore(x => x.Lines).Ignore(x => x.IsChecked)
            .Map(x => x.PersistedIsDeleted, x => x.IsDeleted);
        config.NewConfig<OutboundHeaderModel, OutboundHeaderDetailDto>()
            .Map(x => x.IsDeleted, x => x.PersistedIsDeleted);
        config.NewConfig<OutboundLineDetailDto, OutboundLineModel>()
            .IgnoreNullValues(true).Ignore(x => x.IsChecked)
            .Map(x => x.PersistedIsDeleted, x => x.IsDeleted);
        config.NewConfig<OutboundLineModel, OutboundLineDetailDto>()
            .Map(x => x.IsDeleted, x => x.PersistedIsDeleted);
    }
}
