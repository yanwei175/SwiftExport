using Refit;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand;
using SwiftExport.Contracts.Wrappers;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SwiftExport.Contracts.Apis.DemandPlanning
{
    /// <summary>
    /// 净需求计划工作台 API。客户端 BaseAddress 为 api/demand-planning/net-demands。
    /// </summary>
    public interface INetDemandWorkspaceApi
    {
        /// <summary>查询可建立物料需求的来源需求候选。</summary>
        [Post("/source-demands/candidates/search")]
        Task<ServiceResult<PagedResponseBase<SourceDemandCandidateDto>>> SearchSourceDemandCandidatesAsync(
            [Body] SourceDemandCandidateSearchRequest request,
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

        /// <summary>从来源需求建立物料需求。</summary>
        [Post("/source-demands/establish")]
        Task<ServiceResult<EstablishMaterialDemandsResult>> EstablishFromSourcesAsync(
            [Body] EstablishMaterialDemandsCommand command,
            CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>回退来源对应的活动物料需求修订。</summary>
        [Post("/source-demands/rollback")]
        Task<ServiceResult<RollbackMaterialDemandsResult>> RollbackSourceEstablishmentAsync(
            [Body] RollbackMaterialDemandsCommand command,
            CancellationToken cancellationToken = default(CancellationToken));

        [Post("/search")]
        Task<ServiceResult<PagedResponseBase<NetDemandListItemDto>>> SearchAsync(
            [Body] NetDemandSearchRequest request,
            CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// 查询净需求来源导航分组，筛选口径独立于工作台顶部查询条件。
        /// </summary>
        [Post("/source-groups")]
        Task<ServiceResult<List<NetDemandSourceGroupDto>>> SearchSourceGroupsAsync(
            [Body] NetDemandSourceGroupSearchRequest request,
            CancellationToken cancellationToken = default(CancellationToken));

        [Get("/{uid}")]
        Task<ServiceResult<NetDemandDetailDto>> GetDetailAsync(
            Guid uid,
            CancellationToken cancellationToken = default(CancellationToken));

        [Post("/plans/replace")]
        Task<ServiceResult<NetDemandPlanCommandResult>> ReplacePlanAsync(
            [Body] ReplaceNetDemandPlanCommand command);

        [Post("/plans/apply-product-default")]
        Task<ServiceResult<NetDemandPlanCommandResult>> ApplyDefaultSupplierAsync(
            [Body] ApplyProductDefaultSupplierCommand command);

        [Post("/plans/batch-assign-supplier")]
        Task<ServiceResult<NetDemandPlanCommandResult>> BatchAssignSupplierAsync(
            [Body] BatchAssignSupplierCommand command);

    }
}
