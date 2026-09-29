using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrders.Trace;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces.Procurement;

/// <summary>校验采购读取权限后提供只读追溯用例。</summary>
public interface IPurchaseOrderTraceService
{
    /// <summary>按采购单身份读取完整证据链。</summary>
    Task<ServiceResult<PurchaseOrderTraceDto>> GetAsync(Guid purchaseOrderId,
        CancellationToken cancellationToken = default);
}
