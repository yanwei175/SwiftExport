using SwiftExport.Contracts.DTOs.Inventory.Inbounds.Sources;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Contracts.DTOs.Inventory;

namespace SwiftExport.Application.Interfaces.Inventory.Inbounds;

/// <summary>提供采购入库来源行及供应商筛选候选的只读查询。</summary>
public interface IInboundSourceQueryService
{
    /// <summary>按入库查看权限读取未软删供应商筛选项，包含停用且不依赖采购记录。</summary>
    Task<ServiceResult<PagedResponseBase<InventorySupplierLookupDto>>> GetSupplierOptionsAsync(PagedRequestBase request, CancellationToken token = default);

    /// <summary>按采购来源条件分页选源，不预占收货数量。</summary>
    Task<ServiceResult<PagedResponseBase<InboundSourceCandidateDto>>> SearchSourcesAsync(InboundSourceSearchRequest request, CancellationToken token = default);
}
