using SwiftExport.Contracts.DTOs.Orders;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Orders;
using SwiftExport.Core.Common;

namespace SwiftExport.Application.Interfaces
{
    /// <summary>
    /// 采购跟进服务接口。
    /// </summary>
    public partial interface IFollowUpService
    {
    Task<ServiceResult<FollowUpLogDto>> GetByIdAsync(Guid uid);
    Task<ServiceResult<IReadOnlyList<FollowUpLogDto>>> GetAllAsync();
    Task<ServiceResult<PagedResultDto<FollowUpLogDto>>> GetPagedListAsync(QueryCriteria criteria, int pageIndex, int pageSize, string orderBy = "Uid DESC");
    Task<ServiceResult<Guid>> AddAsync(FollowUpLogDto dto);
    Task<ServiceResult<int>> UpdateAsync(FollowUpLogDto dto);
    Task<ServiceResult<int>> DeleteAsync(Guid uid);
    Task<ServiceResult<BatchResultDto>> BatchSaveAsync(BatchRequestDto<FollowUpLogDto> batchReq);

        /// <summary>
        /// 更新采购跟进信息：当采购行 ReadyQty 变更时，同步更新关联净需求 ReceivedQty。
        /// </summary>
        /// <param name="request">采购跟进更新请求</param>
        /// <returns>是否更新成功</returns>
        Task<ServiceResult<bool>> UpdatePurchaseFollowUpAsync(PurchaseFollowUpUpdateDto request);
    }
}
