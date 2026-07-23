using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.DTOs.Products.Responses;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;
namespace SwiftExport.Application.Interfaces;

public partial interface IProductService : IBaseService<Product, ProductDto>
{

    Task<ServiceResult<ProductBatchSaveResponse>> BatchSaveAsync(ProductBatchSaveRequest batchReq);

    Task<ServiceResult<IReadOnlyList<string>>> GetAllCustomerCodeListAsync();

    // 在这里扩展特有的业务方法
    Task<ServiceResult<ProductDto>> GetBySkuAsync(string sku);

    Task<ServiceResult<PagedResponseBase<ProductDto>>> GetPagedAsync(ProductSearchRequest search);

    Task<ServiceResult<PagedResponseBase<ProductCategoryAssignmentRowDto>>> GetCategoryAssignmentPageAsync(
        ProductCategoryAssignmentSearchRequest request,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<AssignProductsToCategoryResult>> AssignCategoryAsync(
        AssignProductsToCategoryRequest request,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<List<ProductLookupDto>>> SearchLookupAsync(ProductLookupRequest request);
}
