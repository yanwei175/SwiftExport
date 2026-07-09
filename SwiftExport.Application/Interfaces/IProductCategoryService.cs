using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.DTOs.Products.Responses;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;
namespace SwiftExport.Application.Interfaces;

public partial interface IProductCategoryService : IBaseService<ProductCategory, ProductCategoryDto>
{

    Task<ServiceResult<IReadOnlyList<ProductCategoryDto>>> GetAllAsync();
    Task<ServiceResult<PagedResponseBase<ProductCategoryDto>>> GetPagedAsync(ProductCategorySearchRequest request);
    Task<ServiceResult<ProductCategoryBatchSaveResponse>> BatchSaveAsync(ProductCategoryBatchSaveRequest batchReq);


}
