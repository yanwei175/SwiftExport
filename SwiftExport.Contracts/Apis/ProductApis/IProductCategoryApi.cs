using Refit;
using SwiftExport.Contracts.Apis.BaseApi;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.DTOs.Products.Responses;
using SwiftExport.Contracts.Wrappers;
using System.Threading.Tasks;
namespace SwiftExport.Contracts.Apis.ProductApis
{
    public interface IProductCategoryApi : IGenericApi<ProductCategoryDto>
    {
        [Get("/page")]
        Task<ServiceResult<PagedResultDto<ProductCategoryDto>>> GetPagedAsync([Query] ProductCategorySearchRequest request);

        [Post("/batch-save")]
        Task<ServiceResult<ProductCategoryBatchSaveResponse>> BatchSaveAsync([Body] ProductCategoryBatchSaveRequest request);
    }
}