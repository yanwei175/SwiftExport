using Refit;
using SwiftExport.Contracts.Apis.BaseApi;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.DTOs.Products.Responses;
using SwiftExport.Contracts.Wrappers;
using System.Collections.Generic;
using System.Threading.Tasks;
namespace SwiftExport.Contracts.Apis.ProductApis
{
    public interface IProductColorApi : IGenericApi<ProductColorDto>
    {
        [Post("/page")]
        Task<ServiceResult<PagedResponseBase<ProductColorDto>>> GetPagedAsync([Body] ProductColorSearchRequest request);
        [Post("/batch-save")]
        Task<ServiceResult<ProductColorBatchSaveResponse>> BatchSaveAsync([Body] ProductColorBatchSaveRequest request);

        [Get("/all")]
        Task<ServiceResult<List<ProductColorDto>>> GetAllAsync();
    }
}
