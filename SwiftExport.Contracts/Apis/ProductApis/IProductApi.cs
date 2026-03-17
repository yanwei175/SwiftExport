using Refit;
using SwiftExport.Contracts.Apis.BaseApi;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.Wrappers;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SwiftExport.Contracts.Apis.ProductApis
{
    public interface IProductApi : IGenericApi<ProductDto>
    {
        // 1. 获取分类树
        [Get("/product-categories/tree")]
        Task<ServiceResult<List<ProductCategoryDto>>> GetCategoryTreeAsync();

        // 2. 搜索产品 (分页)
        // Refit 会把 SearchDto 里的属性拼接到 URL 参数中
        [Get("/")]
        Task<ServiceResult<PagedResultDto<ProductDto>>> GetProductPagedListAsync([Query] ProductSearchDto search);

        // 3. 获取单个详情
        [Get("/customerCodes")]
        Task<ServiceResult<List<string>>> GetAllCustomerCodeListAsync();


    }
}