using Refit;
using SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand;
using SwiftExport.Contracts.Wrappers;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SwiftExport.Contracts.Apis.DemandPlanning
{
    /// <summary>
    /// 成品需求工作台 API。客户端 BaseAddress 为 api/demand-planning/gross-demands。
    /// </summary>
    public interface IGrossDemandWorkspaceApi
    {
        [Post("/search")]
        Task<ServiceResult<PagedResponseBase<GrossDemandListItemDto>>> SearchAsync([Body] GrossDemandSearchRequest request, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>读取成品需求关联客户的筛选候选，仅需成品需求查看权限。</summary>
        [Get("/customer-options")]
        Task<ServiceResult<List<GrossDemandCustomerOptionDto>>> GetCustomerOptionsAsync(CancellationToken cancellationToken = default(CancellationToken));

        [Post("/source-groups/search")]
        Task<ServiceResult<List<GrossDemandSourceGroupDto>>> SearchSourceGroupsAsync([Body] GrossDemandSourceGroupSearchRequest request, CancellationToken cancellationToken = default(CancellationToken));

        [Get("/{uid}")]
        Task<ServiceResult<GrossDemandDetailDto>> GetDetailAsync(Guid uid, CancellationToken cancellationToken = default(CancellationToken));

        [Post("/manual")]
        Task<ServiceResult<GrossDemandDetailDto>> CreateManualAsync([Body] CreateManualGrossDemandRequest request);

        [Put("/manual/{uid}")]
        Task<ServiceResult<GrossDemandDetailDto>> UpdateManualAsync(Guid uid, [Body] UpdateManualGrossDemandRequest request);

        [Post("/manual/{uid}/close")]
        Task<ServiceResult<GrossDemandDetailDto>> CloseManualAsync(Guid uid, [Body] CloseGrossDemandCommand command);
    }
}
