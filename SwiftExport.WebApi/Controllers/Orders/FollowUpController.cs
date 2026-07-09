using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces;
using SwiftExport.Contracts.DTOs.Orders.Requests;
using SwiftExport.Contracts.DTOs.Orders.Responses;
using SwiftExport.Contracts.DTOs.PurchaseOrders.Requests;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Orders;
using SwiftExport.WebApi.Controllers.Base;
using SwiftExport.WebApi.Filters;
namespace SwiftExport.WebApi.Controllers.Orders;


public class FollowUpController(IFollowUpService service) : BaseController<FollowUpLog, FollowUpLogDto>(service)
{


    [HttpPost("page")]
    public async Task<ServiceResult<PagedResponseBase<FollowUpLogDto>>> GetPagedList([FromBody] FollowUpSearchRequest request)
    {
        return await service.GetPagedAsync(request);
    }

    [HttpPost("batch-save")]
    [PreventDuplicateRequest(WindowSeconds = 5)]
    public async Task<ServiceResult<FollowUpBatchSaveResponse>> BatchSave([FromBody] FollowUpBatchSaveRequest req) => await service.BatchSaveAsync(req);

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
