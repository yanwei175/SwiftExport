using Refit;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.Wrappers;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SwiftExport.Contracts.Apis.DemandPlanning
{
    /// <summary>物料需求工作台的隔离查询与安全回退 API。</summary>
    public interface IMaterialDemandWorkspaceApi
    {
        [Post("/material-search")]
        Task<ServiceResult<PagedResponseBase<MaterialDemandWorkspaceItemDto>>> SearchAsync(
            [Body] MaterialDemandWorkspaceSearchRequest request,
            CancellationToken cancellationToken = default(CancellationToken));

        [Post("/material-source-groups")]
        Task<ServiceResult<List<NetDemandSourceGroupDto>>> SearchSourceGroupsAsync(
            [Body] MaterialDemandSourceGroupSearchRequest request,
            CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>局部更新一条活动物料需求的推荐采购供应商；不更新既有采购单。</summary>
        [Post("/suggested-purchase-supplier")]
        Task<ServiceResult<UpdateSuggestedPurchaseSupplierResult>> UpdateSuggestedPurchaseSupplierAsync(
            [Body] UpdateSuggestedPurchaseSupplierCommand command,
            CancellationToken cancellationToken = default(CancellationToken));

        [Post("/source-demands/rollback")]
        Task<ServiceResult<RollbackMaterialDemandsResult>> RollbackSourceEstablishmentAsync(
            [Body] RollbackMaterialDemandsCommand command,
            CancellationToken cancellationToken = default(CancellationToken));
    }
}
