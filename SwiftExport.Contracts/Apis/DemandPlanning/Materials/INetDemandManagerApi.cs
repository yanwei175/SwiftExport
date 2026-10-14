using Refit;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand.Trace;
using SwiftExport.Contracts.Wrappers;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SwiftExport.Contracts.Apis.DemandPlanning
{
    /// <summary>物料需求管理 API：负责事实查询、来源建立、推荐供应商维护与安全回退。</summary>
    public interface INetDemandManagerApi
    {
        /// <summary>按物料需求 Uid 查询只读拆解、采购及收货证据。</summary>
        [Get("/{uid}/trace")]
        Task<ServiceResult<MaterialDemandTraceDetailDto>> GetTraceAsync(Guid uid,
            CancellationToken cancellationToken = default(CancellationToken));
        /// <summary>查询全部仍包含待建立行的来源单分组。</summary>
        [Get("/source-demands/candidate-groups")]
        Task<ServiceResult<List<SourceDemandCandidateGroupDto>>> GetSourceDemandCandidateGroupsAsync(
            CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>按稳定来源身份完整查询该单全部待建立来源行。</summary>
        [Get("/source-demands/candidates/{sourceType}/{sourceUid}")]
        Task<ServiceResult<List<SourceDemandCandidateDto>>> GetSourceDemandCandidatesBySourceAsync(
            string sourceType,
            Guid sourceUid,
            CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>从成品需求建立物料需求。</summary>
        [Post("/source-demands/establish")]
        Task<ServiceResult<EstablishMaterialDemandsResult>> EstablishFromSourcesAsync(
            [Body] EstablishMaterialDemandsCommand command,
            CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>分页查询已经建立的物料需求事实。</summary>
        [Post("/material-search")]
        Task<ServiceResult<PagedResponseBase<MaterialDemandManagerItemDto>>> SearchAsync(
            [Body] MaterialDemandManagerSearchRequest request,
            CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>查询物料事实的来源导航分组。</summary>
        [Post("/material-source-groups")]
        Task<ServiceResult<List<NetDemandSourceGroupDto>>> SearchSourceGroupsAsync(
            [Body] MaterialDemandSourceGroupSearchRequest request,
            CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>局部更新一条活动物料需求的推荐采购供应商；不更新既有采购单。</summary>
        [Post("/suggested-purchase-supplier")]
        Task<ServiceResult<UpdateSuggestedPurchaseSupplierResult>> UpdateSuggestedPurchaseSupplierAsync(
            [Body] UpdateSuggestedPurchaseSupplierCommand command,
            CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>按来源及物料修订安全回退；存在执行事实时由后端阻断。</summary>
        [Post("/source-demands/rollback")]
        Task<ServiceResult<RollbackMaterialDemandsResult>> RollbackSourceEstablishmentAsync(
            [Body] RollbackMaterialDemandsCommand command,
            CancellationToken cancellationToken = default(CancellationToken));
    }
}
