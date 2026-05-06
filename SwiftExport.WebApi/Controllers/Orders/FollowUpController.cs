using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces;
using SwiftExport.Contracts.DTOs.Orders;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Orders;
using SwiftExport.WebApi.Controllers.Base;

namespace SwiftExport.WebApi.Controllers.Orders
{
    /// <summary>
    /// 采购跟进控制器。
    /// </summary>
    public class FollowUpController(IFollowUpService service)
        : BaseController<FollowUpLog, FollowUpLogDto>(service)
    {
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
