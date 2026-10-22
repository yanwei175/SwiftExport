using SwiftExport.Contracts.DTOs.Inventory.Inbounds.Sources;
using SwiftExport.Contracts.DTOs.Inventory;
using System.Threading;
using System.Threading.Tasks;
using Refit;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Contracts.Apis.Inventory.Inbounds
{
    /// <summary>入库来源只读查询契约；由入库选源服务实现，不复用旧 Workspace 选源语义。</summary>
    public interface IInboundSourceApi
    {
        /// <summary>按入库查看权限分页读取未删除供应商的筛选身份；包含停用供应商，不依赖主数据管理权限或已有采购记录。</summary>
        [Get("/suppliers/picker")]
        Task<ServiceResult<PagedResponseBase<InventorySupplierLookupDto>>> GetSupplierOptionsAsync(
            [Query] PagedRequestBase request, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>返回合法来源行快照，不按仓库筛选，不加更新锁或预占数量。</summary>
        [Post("/search")]
        Task<ServiceResult<PagedResponseBase<InboundSourceCandidateDto>>> SearchAsync(
            [Body] InboundSourceSearchRequest request, CancellationToken cancellationToken = default(CancellationToken));
    }
}
