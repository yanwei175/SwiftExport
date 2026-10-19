using SwiftExport.Contracts.DTOs.OrderTracking.Receipts;
using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces.OrderTracking.Receipts;
using SwiftExport.Contracts.Wrappers;
namespace SwiftExport.WebApi.Controllers.OrderTracking;
/// <summary>Receipts 区域独立 HTTP 边界，组合权限由端点过滤器强制执行。</summary>
[ApiController]
[Route("api/order-tracking/demands")]
public sealed class DemandTrackingReceiptController(IDemandTrackingReceiptService service) : ControllerBase
{
    /// <summary>独立物料区域入口，权限与需求级区域分别登记。</summary>
    [HttpGet("~/api/order-tracking/materials/{netDemandId:guid}/receipts")]
    public Task<ServiceResult<SwiftExport.Contracts.DTOs.OrderTracking.Receipts.DemandTrackingReceiptSectionDto>> GetMaterialReceiptsAsync([FromRoute]Guid netDemandId,CancellationToken cancellationToken)
        =>service.GetMaterialReceiptsAsync(netDemandId,cancellationToken);
    /// <summary>仅加载当前区域，透传需求身份与请求取消。</summary>
    [HttpGet("{grossDemandId:guid}/receipts")]
    public Task<ServiceResult<DemandTrackingReceiptSectionDto>> GetReceiptsAsync([FromRoute]Guid grossDemandId,CancellationToken cancellationToken)
        =>service.GetReceiptsAsync(grossDemandId,cancellationToken);
}
