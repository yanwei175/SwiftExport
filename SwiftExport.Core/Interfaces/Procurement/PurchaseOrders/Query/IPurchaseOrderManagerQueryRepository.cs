using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderManagerDto;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Core.Interfaces.Procurement;

/// <summary>提供采购管理页新链专用的头摘要分页与行摘要查询，不返回旧 Workspace 宽模型。</summary>
public interface IPurchaseOrderManagerQueryRepository
{
    /// <summary>按管理页筛选条件读取新链采购头摘要分页。</summary>
    Task<PagedResponseBase<PurchaseOrderManagerListItemDto>> SearchAsync(
        PurchaseOrderManagerSearchRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>读取指定新链采购单的管理页行摘要。</summary>
    Task<IReadOnlyList<PurchaseOrderManagerLineDto>> GetLinesAsync(
        Guid purchaseOrderId,
        CancellationToken cancellationToken = default);
}
