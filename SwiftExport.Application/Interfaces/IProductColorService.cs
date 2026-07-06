using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces
{
    public partial interface IProductColorService
    {
        Task<ServiceResult<ProductColorDto>> GetByIdAsync(Guid uid);
        Task<ServiceResult<IReadOnlyList<ProductColorDto>>> GetAllAsync();
        Task<ServiceResult<PagedResultDto<ProductColorDto>>> GetPagedAsync(PagedRequestDto request);
        Task<ServiceResult<Guid>> AddAsync(ProductColorDto dto);
        Task<ServiceResult<int>> UpdateAsync(ProductColorDto dto);
        Task<ServiceResult<int>> DeleteAsync(Guid uid);
        Task<ServiceResult<BatchResultDto>> BatchSaveAsync(BatchRequestDto<ProductColorDto> batchReq);


    }
}
