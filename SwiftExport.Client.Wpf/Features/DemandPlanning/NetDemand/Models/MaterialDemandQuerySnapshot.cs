using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.SysEnums;

namespace SwiftExportClient.Features.DemandPlanning.NetDemand.Models;

/// <summary>冻结一次物料需求查询条件；翻页、刷新不读取尚未应用的输入。</summary>
public sealed partial record MaterialDemandQuerySnapshot(
    string Keyword, string[] SourceTypes, string[] SourceNos, string[] Skus, string[] CustomerSkus,
    string SuggestedSupplierKeyword, string ActualSupplierKeyword, string PurchaseOrderNo,
    NetDemandLifecycleStatusEnum? Lifecycle, string QuickView, DateTime? DateFrom, DateTime? DateTo,
    bool? IsCustomerSupplied, Guid[] NetDemandIds, Guid[] GrossDemandIds, Guid[] SourceUids, Guid? SourceUid = null)
{
    /// <summary>为本次请求复制所有条件集合，避免输入变更或外部修改污染已应用条件。</summary>
    public MaterialDemandManagerSearchRequest ForPage(int page, int size) => new()
    {
        Keyword = Keyword, SourceTypes = SourceTypes.ToList(), SourceNos = SourceNos.ToList(),
        Skus = Skus.ToList(), CustomerSkus = CustomerSkus.ToList(),
        SuggestedPurchaseSupplierKeyword = SuggestedSupplierKeyword, ActualPurchaseSupplierKeyword = ActualSupplierKeyword,
        PurchaseOrderNo = PurchaseOrderNo, LifecycleStatus = Lifecycle, QuickView = QuickView,
        DemandRequiredDateFrom = DateFrom, DemandRequiredDateTo = DateTo, IsCustomerSupplied = IsCustomerSupplied,
        NetDemandIds = NetDemandIds.ToList(), GrossDemandIds = GrossDemandIds.ToList(), SourceUids = SourceUids.ToList(),
        SourceUid = SourceUid, PageIndex = SourceUid.HasValue ? 1 : Math.Max(1, page), PageSize = size
    };

    /// <summary>来源模式仅保留来源身份和物料生命周期，避免其他筛选截断父子物料族。</summary>
    public static MaterialDemandQuerySnapshot ForSource(NetDemandSourceGroupModel group, NetDemandLifecycleStatusEnum? lifecycle)
        => new(string.Empty, [group.SourceType], [], [], [], string.Empty, string.Empty, string.Empty,
            lifecycle, "All", null, null, null, [], [], [], group.SourceUid);
}
