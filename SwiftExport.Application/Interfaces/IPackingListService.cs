using SwiftExport.Contracts.DTOs.PackingLists;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Orders;

namespace SwiftExport.Application.Interfaces;

public interface IPackingListService : IBaseService<PackingListHead, PackingListHeadDto>
{
    Task<ServiceResult<PagedResultDto<PackingListHeadDto>>> GetHeadPagedListAsync(PackingListSearchDto search);
    Task<ServiceResult<PackingListDetailDto>> GetDetailByIdAsync(Guid id);
    Task<ServiceResult<Guid>> SaveDetailAsync(PackingListDetailDto request);
    Task<ServiceResult<PackingListPostResultDto>> PostPackingListAsync(PackingListPostRequestDto request);
}
