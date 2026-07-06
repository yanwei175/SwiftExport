using Refit;
using SwiftExport.Contracts.Apis.BaseApi;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.DTOs.Products.Responses;
using SwiftExport.Contracts.Wrappers;
using System.Threading.Tasks;
namespace SwiftExport.Contracts.Apis.ProductApis
{
    public interface IProductColorApi : IGenericApi<ProductColorDto>
    {
        [Get("/page")]
        Task<ServiceResult<PagedResultDto<ProductColorDto>>> GetPagedAsync([Query] ProductColorSearchRequest request);
        [Get("/")]
        Task<ServiceResult<PagedResultDto<ProductColorDto>>> GetPagedRootAsync([Query] ProductColorSearchRequest request);

        [Post("/batch-save")]
        Task<ServiceResult<ProductColorBatchSaveResponse>> BatchSaveAsync([Body] ProductColorBatchSaveRequest request);
    }
}
