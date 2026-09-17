using Mapster;
using SwiftExport.Contracts.DTOs.Shipping.PackingLists;
using SwiftExportClient.Features.Shipping.PackingLists.Models;

namespace SwiftExportClient.Features.Shipping.PackingLists.Mappings;

/// <summary>装箱单查询与页面模型集中映射；写入类型不接收状态、审计和派生事实。</summary>
public sealed class PackingListMappings : IRegister
{
    /// <summary>注册明确的双向读取映射及专用草稿输入映射，不注册网络服务。</summary>
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<PackingListSourcePickerSearchModel, PackingListSourceSearchRequest>();
        config.NewConfig<PackingListSourceCandidateDto, PackingListSourcePickerRowModel>()
            .Ignore(x => x.IsChecked).Ignore(x => x.DraftQty);
        config.NewConfig<PackingListSourcePickerRowModel, PackingListSourceCandidateDto>();
        config.NewConfig<PackingListSourcePickerRowModel, PackingListSourcePickerSelection>()
            .Map(x => x.Source, x => x)
            .Map(x => x.Qty, x => x.AvailableQty ?? 0m);
        // 来源快照只带展示字段；行身份、行号和实际包装数由后续详情接纳动作显式生成。
        config.NewConfig<PackingListSourceCandidateDto, PackingListLineDto>()
            .Map(x => x.IsNonStandardSnapshot, x => x.IsNonStandard)
            .Map(x => x.SourceTypeSnapshot, x => x.SourceType)
            .Map(x => x.SourceDocumentNoSnapshot, x => x.SourceNo)
            .Map(x => x.SourceLineNumberSnapshot, x => x.SourceLineNumber);
        config.NewConfig<PackingListHeaderDto, PackingListHeaderModel>()
            .Ignore(x => x.IsChecked).Ignore(x => x.IsSelected).Ignore(x => x.IsReadOnly)
            .Map(x => x.PersistedIsDeleted, x => x.IsDeleted)
            .Ignore(x => x.Lines);
        config.NewConfig<PackingListHeaderModel, PackingListHeaderDto>()
            .Map(x => x.IsDeleted, x => x.PersistedIsDeleted);
        config.NewConfig<PackingListLineDto, PackingListLineModel>()
            .Ignore(x => x.IsChecked).Ignore(x => x.IsSelected).Ignore(x => x.IsReadOnly)
            .Map(x => x.PersistedIsDeleted, x => x.IsDeleted);
        config.NewConfig<PackingListLineModel, PackingListLineDto>()
            .Map(x => x.IsDeleted, x => x.PersistedIsDeleted);
        config.NewConfig<PackingListManagerListItemDto, PackingListManagerRowModel>()
            .Ignore(x => x.IsChecked).Ignore(x => x.IsSelected).Ignore(x => x.IsReadOnly)
            .Map(x => x.PersistedIsDeleted, x => x.IsDeleted)
            .Map(x => x.PackingListNo, x => x.PackingListNo ?? string.Empty)
            .Map(x => x.CustomerNameSnapshot, x => x.CustomerNameSnapshot ?? string.Empty)
            .Map(x => x.InvoiceNosSummary, x => x.InvoiceNosSummary ?? string.Empty);
        config.NewConfig<PackingListManagerRowModel, PackingListManagerListItemDto>()
            .Map(x => x.IsDeleted, x => x.PersistedIsDeleted);
        config.NewConfig<PackingListHeaderModel, PackingListDraftHeaderInput>();
        config.NewConfig<PackingListLineModel, PackingListDraftLineInput>()
            .Map(x => x.ExpectedVersion, x => x.IsNew ? 0 : x.Version);
        config.NewConfig<PackingListLineModel, PackingListDeletedLineInput>()
            .Map(x => x.ExpectedVersion, x => x.Version);
        config.NewConfig<PackingListManagerSearchModel, PackingListManagerSearchRequest>();
    }
}
