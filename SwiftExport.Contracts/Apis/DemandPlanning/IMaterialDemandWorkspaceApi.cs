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

        [Post("/source-demands/rollback")]
        Task<ServiceResult<RollbackMaterialDemandsResult>> RollbackSourceEstablishmentAsync(
            [Body] RollbackMaterialDemandsCommand command,
            CancellationToken cancellationToken = default(CancellationToken));
    }
}
