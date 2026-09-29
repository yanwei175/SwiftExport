using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrders.Trace;

namespace SwiftExport.Core.Interfaces.Procurement;

/// <summary>读取单张采购单的历史证据，不修改采购、需求或入库事实。</summary>
public interface IPurchaseOrderTraceRepository
{
    /// <summary>按采购单 Uid 返回证据链，不存在时返回空。</summary>
    Task<PurchaseOrderTraceDto?> GetAsync(Guid purchaseOrderId, CancellationToken cancellationToken = default);
}
