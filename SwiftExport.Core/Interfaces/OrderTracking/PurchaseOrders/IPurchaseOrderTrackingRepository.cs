using SwiftExport.Contracts.DTOs.OrderTracking.PurchaseOrders;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderProgress;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Core.Interfaces.OrderTracking.PurchaseOrders;

/// <summary>采购单视角的独立只读投影，不改变需求贡献、采购状态或反馈事实。</summary>
public interface IPurchaseOrderTrackingRepository
{
    /// <summary>数据库筛选、计数和分页；缺权维度不读取对应事实。</summary>
    Task<PagedResponseBase<PurchaseOrderTrackingItemDto>> SearchAsync(PurchaseOrderTrackingSearchRequest request,bool canViewProgress,bool canViewReceipts,CancellationToken cancellationToken=default);
    /// <summary>按采购单读取全部有效子行，维度权限由服务明确传入。</summary>
    Task<IReadOnlyList<PurchaseOrderTrackingLineDto>> GetLinesAsync(Guid purchaseOrderId,bool canViewProgress,bool canViewReceipts,CancellationToken cancellationToken=default);
    /// <summary>核实采购身份，区分缺失单据和有效空结果。</summary>
    Task<bool> ExistsAsync(Guid purchaseOrderId,CancellationToken cancellationToken=default);
    /// <summary>整采购行正式收货及完整证据，不分摊为各需求实收。</summary>
    Task<PurchaseOrderTrackingReceiptsDto> GetReceiptsAsync(Guid purchaseOrderId,CancellationToken cancellationToken=default);
    /// <summary>采购行完整不可变反馈历史，包括仍可追溯的已删除行。</summary>
    Task<List<SupplierProgressReportDto>> GetReportsAsync(Guid purchaseOrderId,CancellationToken cancellationToken=default);
}
