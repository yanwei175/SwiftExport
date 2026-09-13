using Mapster;
using SwiftExport.Contracts.DTOs.Inventory.Inbounds;
using SwiftExportClient.Features.Inventory.Inbounds.Models;

namespace SwiftExportClient.Features.Inventory.Inbounds.Mappings;

/// <summary>集中映射入库详情与编辑模型，排除 UI 状态和头行集合的隐式复制。</summary>
public sealed class InboundManagerMappings : IRegister
{
    /// <inheritdoc/>
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<InboundSourceCandidateDto, InboundSourcePickerRowModel>().Ignore(x => x.IsChecked);
        config.NewConfig<InboundSourcePickerRowModel, InboundSourceCandidateDto>();
        config.NewConfig<InboundSourceCandidateDto, InboundLineDetailDto>()
            .Map(x => x.SkuSnapshot, x => x.Sku)
            .Map(x => x.DescriptionSnapshot, x => x.Description)
            .Map(x => x.UnitSnapshot, x => x.Unit);
        config.NewConfig<InboundHeaderDetailDto, InboundHeaderModel>()
            .IgnoreNullValues(true).Ignore(x => x.Lines).Ignore(x => x.IsChecked)
            .Map(x => x.PersistedIsDeleted, x => x.IsDeleted);
        config.NewConfig<InboundHeaderModel, InboundHeaderDetailDto>()
            .Map(x => x.IsDeleted, x => x.PersistedIsDeleted);
        config.NewConfig<InboundLineDetailDto, InboundLineModel>()
            .IgnoreNullValues(true).Ignore(x => x.IsChecked)
            .Map(x => x.PersistedIsDeleted, x => x.IsDeleted);
        config.NewConfig<InboundLineModel, InboundLineDetailDto>()
            .Map(x => x.IsDeleted, x => x.PersistedIsDeleted);
    }
}
