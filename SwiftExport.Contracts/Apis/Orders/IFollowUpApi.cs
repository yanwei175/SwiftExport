using Refit;
using SwiftExport.Contracts.Apis.BaseApi;
using SwiftExport.Contracts.DTOs.Orders.Requests;
using SwiftExport.Contracts.DTOs.PurchaseOrders.Requests;
using SwiftExport.Contracts.Wrappers;
using System.Threading.Tasks;
namespace SwiftExport.Contracts.Apis.Orders
{
    /// <summary>
    /// 采购跟进 API。
    /// </summary>
    public interface IFollowUpApi : IGenericApi<FollowUpLogDto>
    {
        [Post("/page")]
        Task<ServiceResult<PagedResponseBase<FollowUpLogDto>>> GetPagedAsync([Body] FollowUpSearchRequest request);
        /// <summary>
        /// 更新采购跟进：当采购行 ReadyQty 更新时，同步更新关联净需求 ReceivedQty。
        /// </summary>
        /// <param name="request">采购跟进更新请求</param>
        /// <returns>是否成功</returns>
        [Post("/purchase/update")]
        Task<ServiceResult<bool>> UpdatePurchaseFollowUpAsync([Body] PurchaseFollowUpUpdateDto request);
    }
}
