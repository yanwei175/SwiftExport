using System.Threading;
using System.Threading.Tasks;
using Refit;
using SwiftExport.Contracts.DTOs.Inventory.Inbounds;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Contracts.Apis.Inventory
{
    /// <summary>新入库来源只读查询契约；后端待实现，不复用旧 Workspace 选源语义。</summary>
    public interface IInboundSourceApi
    {
        /// <summary>返回合法来源行快照，不按仓库筛选，不加更新锁或预占数量。</summary>
        [Post("/search")]
        Task<ServiceResult<PagedResponseBase<InboundSourceCandidateDto>>> SearchAsync(
            [Body] InboundSourceSearchRequest request, CancellationToken cancellationToken = default(CancellationToken));
    }
}
