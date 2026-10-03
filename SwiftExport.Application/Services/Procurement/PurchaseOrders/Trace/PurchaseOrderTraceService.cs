using SwiftExport.Application.Contexts;
using SwiftExport.Core.Interfaces.Data;
using SwiftExport.Application.Interfaces.Procurement;
using SwiftExport.Application.Services.Procurement.PurchaseOrders.Support;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrders.Trace;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.Procurement;

namespace SwiftExport.Application.Services.Procurement.PurchaseOrders.Trace;

/// <summary>采购只读追溯服务；沿用订单读取权限和统一错误边界。</summary>
public sealed class PurchaseOrderTraceService(ServiceBaseContext context, ProcurementOrderReadSupport reads,
    IPurchaseOrderTraceRepository repository) : IPurchaseOrderTraceService
{
    /// <inheritdoc/>
    public Task<ServiceResult<PurchaseOrderTraceDto>> GetAsync(Guid purchaseOrderId,
        CancellationToken cancellationToken = default)
        => reads.ReadAsync("Read", async () =>
        {
            if (purchaseOrderId == Guid.Empty)
                throw new InvalidOperationException("采购单标识无效。");
            try
            {
                return await repository.GetAsync(purchaseOrderId, cancellationToken)
                    ?? throw new InvalidOperationException("采购单不存在。");
            }
            catch (Exception ex) when (context.Session.Errors.Classify(ex) == DatabaseFailureKind.UnverifiedReceiptHistory)
            {
                // 共享历史门禁拒绝不完整反向时，明确提示未知收货量，避免只读页显示零。
                throw new InvalidOperationException("正式入库反向历史需核实，收货量未知。", ex);
            }
        }, cancellationToken);
}
