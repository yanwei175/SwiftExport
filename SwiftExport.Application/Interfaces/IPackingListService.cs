using SwiftExport.Contracts.DTOs.Orders;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Orders;

namespace SwiftExport.Application.Interfaces;

public interface IPackingListService : IBaseService<PackingListHead, PackingListDto>
{
    Task<ServiceResult<PagedResultDto<PackingListDto>>> GetPagedListAsync(PackingListSearchDto search);
    Task<ServiceResult<PackingListDetailDto>> GetDetailByIdAsync(int id);
    Task<ServiceResult<int>> SaveDetailAsync(PackingListDetailDto request);
    Task<ServiceResult<PackingListPostResultDto>> PostPackingListAsync(PackingListPostRequestDto request);
}
