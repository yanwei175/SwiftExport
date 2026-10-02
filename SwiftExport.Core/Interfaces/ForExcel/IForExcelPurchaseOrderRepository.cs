using SwiftExport.Contracts.DTOs.ForExcel.PurchaseOrders;
namespace SwiftExport.Core.Interfaces.ForExcel;

/// <summary>采购导入客户、供应商、单位及精确客户型号查询；创建时共享调用方事务。</summary>
public interface IForExcelPurchaseOrderRepository
{
    /// <summary>按客户代码返回未删除候选，不丢弃停用或歧义记录。</summary>
    Task<IReadOnlyList<PurchaseOrderImportCustomerDto>> FindCustomersAsync(string code, CancellationToken token);
    /// <summary>按稳定身份读取实际供应商。</summary>
    Task<PurchaseOrderImportSupplierDto?> FindSupplierAsync(Guid uid, CancellationToken token);
    /// <summary>按客户和去首尾空格、忽略大小写的客户型号批量查询全部关联。</summary>
    Task<IReadOnlyList<PurchaseOrderImportProductCandidateDto>> MatchProductsAsync(Guid customerId, string[] skus, CancellationToken token);
    /// <summary>返回唯一启用的单位代码，缺失或歧义时为空。</summary>
    Task<string?> FindUnitAsync(string unit, CancellationToken token);
}
