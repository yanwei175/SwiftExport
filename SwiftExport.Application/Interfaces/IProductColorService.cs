using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Common;

namespace SwiftExport.Application.Interfaces
{
    public partial interface IProductColorService
    {
    Task<ServiceResult<ProductColorDto>> GetByIdAsync(Guid uid);
    Task<ServiceResult<IReadOnlyList<ProductColorDto>>> GetAllAsync();
    Task<ServiceResult<PagedResultDto<ProductColorDto>>> GetPagedListAsync(QueryCriteria criteria, int pageIndex, int pageSize, string orderBy = "Uid DESC");
    Task<ServiceResult<Guid>> AddAsync(ProductColorDto dto);
    Task<ServiceResult<int>> UpdateAsync(ProductColorDto dto);
    Task<ServiceResult<int>> DeleteAsync(Guid uid);
    Task<ServiceResult<BatchResultDto>> BatchSaveAsync(BatchRequestDto<ProductColorDto> batchReq);


    }
}
