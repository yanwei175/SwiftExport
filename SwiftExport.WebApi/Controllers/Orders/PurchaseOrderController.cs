using Microsoft.AspNetCore.Mvc;
using Refit;
using SwiftExport.Application.Interfaces;
using SwiftExport.Contracts.DTOs.Orders;
using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Common;
using SwiftExport.WebApi.Filters;

namespace SwiftExport.WebApi.Controllers.Orders
{
    /// <summary>
    /// 采购订单控制器。
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class PurchaseOrderController(IPurchaseOrderService service)
        : ControllerBase
    {
        [HttpGet("{id}")]
        public async Task<ServiceResult<PurchaseOrderHeadDto>> GetById(Guid id) => await service.GetByIdAsync(id);

        [HttpPost]
        [PreventDuplicateRequest]
        public async Task<ServiceResult<Guid>> Create([FromBody] PurchaseOrderHeadDto dto) => await service.AddAsync(dto);

        [HttpPut("{id}")]
        [PreventDuplicateRequest]
        public async Task<ServiceResult<int>> Update(Guid id, [FromBody] PurchaseOrderHeadDto dto) => await service.UpdateAsync(dto);

        [HttpDelete("{id}")]
        [PreventDuplicateRequest]
        public async Task<ServiceResult<int>> Delete(Guid id) => await service.DeleteAsync(id);

        [HttpPost("page")]
        public async Task<ServiceResult<PagedResultDto<PurchaseOrderHeadDto>>> GetPagedList([FromBody] QueryCriteria criteria, [FromQuery] int pageIndex = 1, [FromQuery] int pageSize = 500)
            => await service.GetPagedListAsync(criteria, pageIndex, pageSize);

        [HttpPost("batch-save")]
        [PreventDuplicateRequest(WindowSeconds = 5)]
        public async Task<ServiceResult<BatchResultDto>> BatchSave([FromBody] BatchRequestDto<PurchaseOrderHeadDto> req) => await service.BatchSaveAsync(req);

        [HttpPost("search")]
        public async Task<ServiceResult<PagedResultDto<PurchaseOrderHeadDto>>> GetPagedListAsync([FromBody] PurchaseOrderSearchDto search)
        {
            return await service.GetPagedListAsync(search);
        }

        [HttpGet("{id}/detail")]
        public async Task<ServiceResult<PurchaseOrderDetailDto>> GetDetailByIdAsync(Guid id)
        {
            return await service.GetDetailByHeadIdAsync(id);
        }

        [HttpGet("by-orderno/{orderNo}")]
        public async Task<ServiceResult<PurchaseOrderDetailDto>> GetDetailByOrderNoAsync(string orderNo)
        {
            return await service.GetDetailByOrderNoAsync(orderNo);
        }

        [HttpGet("by-pono/{poNo}")]
        public async Task<ServiceResult<PurchaseOrderDetailDto>> GetDetailByPoNoAsync(string poNo)
        {
            return await service.GetDetailByBoNoAsync(poNo);
        }
        [HttpGet("awaiting-inbound-suppliers")]
       public async Task<ServiceResult<List<SupplierDto>>> GetAwatingInboudSuppliersAsync()
        {
            return await service.GetAwaitingInboundSuppliersAsync();
        }

        [HttpPost("detail/save")]
        public async Task<ServiceResult<Guid>> SaveDetailAsync([FromBody] PurchaseOrderDetailDto request)
        {
            return await service.SaveDetailAsync(request);
        }
    }
}
