using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderManagerDto;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces.Procurement;

/// <summary>采购管理页新链只读用例；与详情、确认命令及旧 Workspace 查询保持隔离。</summary>
public interface IPurchaseOrderManagerQueryService
{
    /// <summary>校验管理页筛选并返回新链采购头摘要分页。</summary>
    Task<ServiceResult<PagedResponseBase<PurchaseOrderManagerListItemDto>>> SearchAsync(
        PurchaseOrderManagerSearchRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>返回指定新链采购单的管理页行摘要。</summary>
    Task<ServiceResult<IReadOnlyList<PurchaseOrderManagerLineDto>>> GetLinesAsync(
        Guid purchaseOrderId,
        CancellationToken cancellationToken = default);
}
