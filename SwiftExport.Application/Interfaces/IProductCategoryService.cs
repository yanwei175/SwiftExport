using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Common;

using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.DTOs.Products.Responses;
namespace SwiftExport.Application.Interfaces
{
    public partial interface IProductCategoryService
    {
    Task<ServiceResult<ProductCategoryDto>> GetByIdAsync(Guid uid);
    Task<ServiceResult<IReadOnlyList<ProductCategoryDto>>> GetAllAsync();
    Task<ServiceResult<PagedResultDto<ProductCategoryDto>>> GetPagedListAsync(QueryCriteria criteria, int pageIndex, int pageSize, string orderBy = "Uid DESC");
    Task<ServiceResult<Guid>> AddAsync(ProductCategoryDto dto);
    Task<ServiceResult<int>> UpdateAsync(ProductCategoryDto dto);
    Task<ServiceResult<int>> DeleteAsync(Guid uid);
    Task<ServiceResult<ProductCategoryBatchSaveResponse>> BatchSaveAsync(ProductCategoryBatchSaveRequest batchReq);


    }
}
