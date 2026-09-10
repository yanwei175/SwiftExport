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

        /// <summary>查询全部仍包含待采购物料的来源单分组。</summary>
        [Get("/purchase-candidates/source-groups")]
        Task<ServiceResult<List<PurchaseCandidateSourceGroupDto>>> GetPurchaseCandidateSourceGroupsAsync(
            CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>按稳定来源身份或全部来源完整查询待采购物料，不分页。</summary>
        [Post("/purchase-candidates")]
        Task<ServiceResult<List<MaterialDemandWorkspaceItemDto>>> GetPurchaseCandidatesAsync(
            [Body] PurchaseCandidateQuery request,
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
