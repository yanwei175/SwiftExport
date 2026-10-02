using SwiftExport.Contracts.DTOs.ForExcel.PurchaseOrders;
using SwiftExport.Contracts.Wrappers;
namespace SwiftExport.Application.Interfaces.ForExcel;

/// <summary>采购导入只读检查，不负责工作簿解析、需求关联或草稿写入。</summary>
public interface IForExcelPurchaseOrderService
{
    /// <summary>检查单头、数值及标准行身份，返回逐行问题；取消向调用方传播。</summary>
    Task<ServiceResult<CheckPurchaseOrderImportResponse>> CheckAsync(CheckPurchaseOrderImportRequest request, CancellationToken token = default);
}
