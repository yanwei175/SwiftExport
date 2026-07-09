using SwiftExport.Contracts.DTOs.PackingLists;
using SwiftExport.Contracts.DTOs.PackingLists.Requests;
using SwiftExport.Contracts.DTOs.PackingLists.Responses;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Orders;
namespace SwiftExport.Application.Interfaces;

public partial interface IPackingListService : IBaseService<PackingListHead, PackingListHeadDto>
{

    Task<ServiceResult<IReadOnlyList<PackingListHeadDto>>> GetAllAsync();
    Task<ServiceResult<PackingListBatchSaveResponse>> BatchSaveAsync(PackingListBatchSaveRequest batchReq);
    Task<ServiceResult<PagedResponseBase<PackingListHeadDto>>> GetHeadPagedListAsync(PackingListSearchRequest search);
    Task<ServiceResult<PackingListDetailDto>> GetDetailByIdAsync(Guid id);
    Task<ServiceResult<Guid>> SaveDetailAsync(PackingListDetailDto request);
    Task<ServiceResult<PackingListPostResultDto>> PostPackingListAsync(PackingListPostRequestDto request);
}
