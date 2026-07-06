using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.Wrappers;

using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.DTOs.Products.Responses;
namespace SwiftExport.Application.Interfaces
{
    public partial interface IProductColorService
    {
        Task<ServiceResult<ProductColorDto>> GetByIdAsync(Guid uid);
        Task<ServiceResult<IReadOnlyList<ProductColorDto>>> GetAllAsync();
        Task<ServiceResult<PagedResultDto<ProductColorDto>>> GetPagedAsync(ProductColorSearchRequest request);
        Task<ServiceResult<Guid>> AddAsync(ProductColorDto dto);
        Task<ServiceResult<int>> UpdateAsync(ProductColorDto dto);
        Task<ServiceResult<int>> DeleteAsync(Guid uid);
        Task<ServiceResult<ProductColorBatchSaveResponse>> BatchSaveAsync(ProductColorBatchSaveRequest batchReq);


    }
}
