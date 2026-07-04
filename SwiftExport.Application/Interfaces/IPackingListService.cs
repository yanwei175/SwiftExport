using SwiftExport.Contracts.DTOs.PackingLists;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Orders;
using SwiftExport.Core.Common;

namespace SwiftExport.Application.Interfaces;

public partial interface IPackingListService
{
    Task<ServiceResult<PackingListHeadDto>> GetByIdAsync(Guid uid);
    Task<ServiceResult<IReadOnlyList<PackingListHeadDto>>> GetAllAsync();
    Task<ServiceResult<PagedResultDto<PackingListHeadDto>>> GetPagedListAsync(QueryCriteria criteria, int pageIndex, int pageSize, string orderBy = "Uid DESC");
    Task<ServiceResult<Guid>> AddAsync(PackingListHeadDto dto);
    Task<ServiceResult<int>> UpdateAsync(PackingListHeadDto dto);
    Task<ServiceResult<int>> DeleteAsync(Guid uid);
    Task<ServiceResult<BatchResultDto>> BatchSaveAsync(BatchRequestDto<PackingListHeadDto> batchReq);

    Task<ServiceResult<PagedResultDto<PackingListHeadDto>>> GetHeadPagedListAsync(PackingListSearchDto search);
    Task<ServiceResult<PackingListDetailDto>> GetDetailByIdAsync(Guid id);
    Task<ServiceResult<Guid>> SaveDetailAsync(PackingListDetailDto request);
    Task<ServiceResult<PackingListPostResultDto>> PostPackingListAsync(PackingListPostRequestDto request);
}
