using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.DTOs.Products.Responses;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;
namespace SwiftExport.Application.Interfaces;

public partial interface IProductColorService : IBaseService<ProductColor, ProductColorDto>
{

    Task<ServiceResult<IReadOnlyList<ProductColorDto>>> GetAllAsync();
    Task<ServiceResult<PagedResponseBase<ProductColorDto>>> GetPagedAsync(ProductColorSearchRequest request);
    Task<ServiceResult<ProductColorBatchSaveResponse>> BatchSaveAsync(ProductColorBatchSaveRequest batchReq);


}
