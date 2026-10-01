using SwiftExport.Contracts.DTOs.ForExcel.SalesOrders;
namespace SwiftExport.Core.Interfaces.ForExcel;

/// <summary>Excel 销售导入所需的客户、精确型号及重复订单只读查询。</summary>
public interface IForExcelSalesOrderRepository
{
    /// <summary>按客户业务代码取得未删除客户，保留多候选供服务拒绝歧义。</summary>
    Task<IReadOnlyList<SalesOrderImportCustomerDto>> FindCustomersAsync(string customerCode, CancellationToken token);
    /// <summary>按客户身份及客户型号批量读取关联，包含停用或断链候选以解释失败。</summary>
    Task<IReadOnlyList<SalesOrderImportProductCandidateDto>> MatchProductsAsync(Guid customerId, string[] customerSkus, CancellationToken token);
    /// <summary>同客户下匹配任一非空发票号或 PO 号；不把商业编号当唯一键。</summary>
    Task<IReadOnlyList<SalesOrderImportExistingOrderDto>> FindExistingOrdersAsync(Guid customerId, string invoiceNo, string customerPoNo, CancellationToken token);
}
