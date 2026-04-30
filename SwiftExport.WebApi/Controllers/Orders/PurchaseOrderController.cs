using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces;
using SwiftExport.Contracts.DTOs.Orders;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Orders;
using SwiftExport.WebApi.Controllers.Base;

namespace SwiftExport.WebApi.Controllers.Orders
{
    /// <summary>
    /// 采购订单控制器。
    /// </summary>
    public class PurchaseOrderController(IPurchaseOrderService service)
        : BaseController<PurchaseOrderHead, PurchaseOrderDto>(service)
    {
        [HttpPost("search")]
        public async Task<ServiceResult<PagedResultDto<PurchaseOrderDto>>> GetPagedListAsync([FromBody] PurchaseOrderSearchDto search)
        {
            return await service.GetPagedListAsync(search);
        }

        [HttpGet("{id}/detail")]
        public async Task<ServiceResult<PurchaseOrderDetailDto>> GetDetailByIdAsync(int id)
        {
            return await service.GetDetailByIdAsync(id);
        }

        [HttpGet("by-orderno/{orderNo}")]
        public async Task<ServiceResult<PurchaseOrderDetailDto>> GetDetailByOrderNoAsync(string orderNo)
        {
            return await service.GetDetailByOrderNoAsync(orderNo);
        }

        [HttpGet("by-pono/{poNo}")]
        public async Task<ServiceResult<PurchaseOrderDetailDto>> GetDetailByPoNoAsync(string poNo)
        {
            return await service.GetDetailByPoNoAsync(poNo);
        }

        [HttpPost("detail/save")]
        public async Task<ServiceResult<int>> SaveDetailAsync([FromBody] PurchaseOrderDetailDto request)
        {
            return await service.SaveDetailAsync(request);
        }
    }
}
