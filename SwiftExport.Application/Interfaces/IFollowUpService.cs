using SwiftExport.Contracts.DTOs.Orders.Requests;
using SwiftExport.Contracts.DTOs.Orders.Responses;
using SwiftExport.Contracts.DTOs.PurchaseOrders.Requests;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Orders;
namespace SwiftExport.Application.Interfaces;

/// <summary>
/// 采购跟进服务接口。
/// </summary>
public partial interface IFollowUpService : IBaseService<FollowUpLog, FollowUpLogDto>
{

    Task<ServiceResult<IReadOnlyList<FollowUpLogDto>>> GetAllAsync();
    Task<ServiceResult<PagedResponseBase<FollowUpLogDto>>> GetPagedAsync(FollowUpSearchRequest request);

    Task<ServiceResult<FollowUpBatchSaveResponse>> BatchSaveAsync(FollowUpBatchSaveRequest batchReq);

    /// <summary>
    /// 更新采购跟进信息：当采购行 ReadyQty 变更时，同步更新关联净需求 ReceivedQty。
    /// </summary>
    /// <param name="request">采购跟进更新请求</param>
    /// <returns>是否更新成功</returns>
    Task<ServiceResult<bool>> UpdatePurchaseFollowUpAsync(PurchaseFollowUpUpdateDto request);
}
