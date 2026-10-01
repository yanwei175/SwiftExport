using SwiftExport.Contracts.DTOs.ForExcel.SalesOrders;
using SwiftExport.Contracts.Wrappers;
namespace SwiftExport.Application.Interfaces.ForExcel;

/// <summary>已解析 Excel 销售数据的匹配和只读检查边界。</summary>
public interface IForExcelSalesOrderService
{
    /// <summary>检查身份、输入、产品及重复单据，返回问题而不执行保存。</summary>
    Task<ServiceResult<CheckSalesOrderImportResponse>> CheckAsync(CheckSalesOrderImportRequest request, CancellationToken token = default);
}
