using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces.ForExcel;
using SwiftExport.Contracts.DTOs.ForExcel.PurchaseOrders;
using SwiftExport.Contracts.Wrappers;
namespace SwiftExport.WebApi.Controllers.ForExcel;

/// <summary>采购 Excel 导入只读 HTTP 边界，业务检查由 Application 承担。</summary>
[ApiController, Authorize, Route("api/for-excel/purchase-orders")]
public sealed class ForExcelPurchaseOrderController(IForExcelPurchaseOrderService service) : ControllerBase
{
    /// <summary>转发已解析数据并透传取消；字段问题由 CanSave 与 Issues 表达。</summary>
    [HttpPost("check")]
    public Task<ServiceResult<CheckPurchaseOrderImportResponse>> CheckAsync([FromBody] CheckPurchaseOrderImportRequest request,
        CancellationToken cancellationToken) => service.CheckAsync(request, cancellationToken);
}
