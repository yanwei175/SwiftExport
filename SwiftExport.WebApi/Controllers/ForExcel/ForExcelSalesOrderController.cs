using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces.ForExcel;
using SwiftExport.Contracts.DTOs.ForExcel.SalesOrders;
using SwiftExport.Contracts.Wrappers;
namespace SwiftExport.WebApi.Controllers.ForExcel;

/// <summary>Excel 销售导入只读 HTTP 边界，不读取工作簿或执行保存。</summary>
[ApiController, Authorize, Route("api/for-excel/sales-orders")]
public sealed class ForExcelSalesOrderController(IForExcelSalesOrderService service) : ControllerBase
{
    /// <summary>检查已解析数据；逐行业务问题由成功响应中的 CanSave 和 Issues 表达。</summary>
    [HttpPost("check")]
    public Task<ServiceResult<CheckSalesOrderImportResponse>> CheckAsync([FromBody] CheckSalesOrderImportRequest request,
        CancellationToken cancellationToken) => service.CheckAsync(request,cancellationToken);
}
