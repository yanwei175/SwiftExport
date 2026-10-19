using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces.OrderTracking.MaterialDetail;
using SwiftExport.Contracts.DTOs.OrderTracking.MaterialDetail;
using SwiftExport.Contracts.Wrappers;
namespace SwiftExport.WebApi.Controllers.OrderTracking;
/// <summary>单物料只读详情HTTP入口，历史可追溯，权限独立于采购和收货。</summary>
[ApiController]
[Route("api/order-tracking/materials")]
public sealed class MaterialTrackingDetailController(IMaterialTrackingDetailService service) : ControllerBase
{
    /// <summary>按物料身份加载快照及回退说明。</summary>
    [HttpGet("{netDemandId:guid}")]
    public Task<ServiceResult<MaterialTrackingDetailDto>> GetMaterialDetailAsync([FromRoute]Guid netDemandId,CancellationToken cancellationToken)
        =>service.GetMaterialDetailAsync(netDemandId,cancellationToken);
}
