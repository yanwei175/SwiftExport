using SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.DemandPlanning;

namespace SwiftExport.Core.Interfaces.DemandPlanning.Materials.Query;

/// <summary>提供物料需求工作台与来源导航查询。</summary>
public interface IMaterialDemandQueryRepository
{
    /// <summary>查询不依赖计划分配表的物料需求工作台列表。</summary>
    Task<PagedResponseBase<MaterialDemandManagerItemDto>> SearchManagerAsync(
        MaterialDemandManagerSearchRequest request,
        CancellationToken cancellationToken = default, bool includePurchaseDetails = true);
    /// <summary>按来源事实汇总物料需求工作台左侧导航。</summary>
    Task<IReadOnlyList<NetDemandSourceGroupDto>> SearchMaterialSourceGroupsAsync(
        MaterialDemandSourceGroupSearchRequest request,
        CancellationToken cancellationToken = default);
}
