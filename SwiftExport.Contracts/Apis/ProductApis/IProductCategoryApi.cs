using Refit;
using SwiftExport.Contracts.Apis.BaseApi;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.Wrappers;
using System.Threading.Tasks;
namespace SwiftExport.Contracts.Apis.ProductApis
{
    public interface IProductCategoryApi : IGenericApi<ProductCategoryDto>
    {
        [Get("/page")]
        Task<ServiceResult<PagedResultDto<ProductCategoryDto>>> GetPagedAsync([Query] PagedRequestDto request);
    }
}