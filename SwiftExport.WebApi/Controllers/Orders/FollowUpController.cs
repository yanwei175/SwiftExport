using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces;
using SwiftExport.Contracts.DTOs.Orders;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Common;
using SwiftExport.WebApi.Filters;

namespace SwiftExport.WebApi.Controllers.Orders
{
    /// <summary>
    /// 采购跟进控制器。
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class FollowUpController(IFollowUpService service)
        : ControllerBase
    {
        [HttpGet("{id}")]
        public async Task<ServiceResult<FollowUpLogDto>> GetById(Guid id) => await service.GetByIdAsync(id);

        [HttpPost]
        [PreventDuplicateRequest]
        public async Task<ServiceResult<Guid>> Create([FromBody] FollowUpLogDto dto) => await service.AddAsync(dto);

        [HttpPut("{id}")]
        [PreventDuplicateRequest]
        public async Task<ServiceResult<int>> Update(Guid id, [FromBody] FollowUpLogDto dto) => await service.UpdateAsync(dto);

        [HttpDelete("{id}")]
        [PreventDuplicateRequest]
        public async Task<ServiceResult<int>> Delete(Guid id) => await service.DeleteAsync(id);

        [HttpPost("page")]
        public async Task<ServiceResult<PagedResultDto<FollowUpLogDto>>> GetPagedList([FromBody] QueryCriteria criteria, [FromQuery] int pageIndex = 1, [FromQuery] int pageSize = 500)
            => await service.GetPagedListAsync(criteria, pageIndex, pageSize);

        [HttpPost("batch-save")]
        [PreventDuplicateRequest(WindowSeconds = 5)]
        public async Task<ServiceResult<BatchResultDto>> BatchSave([FromBody] BatchRequestDto<FollowUpLogDto> req) => await service.BatchSaveAsync(req);

        /// <summary>
        /// 更新采购跟进：当采购行 ReadyQty 更新时，同步更新关联净需求 ReceivedQty。
        /// </summary>
        /// <param name="request">采购跟进更新请求</param>
        /// <returns>是否成功</returns>
        [HttpPost("purchase/update")]
        public async Task<ServiceResult<bool>> UpdatePurchaseFollowUpAsync([FromBody] PurchaseFollowUpUpdateDto request)
        {
            return await service.UpdatePurchaseFollowUpAsync(request);
        }
    }
}
