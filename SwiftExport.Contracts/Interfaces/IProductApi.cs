using Refit;
using SwiftExport.Contracts.DTOs;
using SwiftExport.Contracts.DTOs.Product;
using SwiftExport.Contracts.Wrappers;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SwiftExport.Contracts.Interfaces
{
    public interface IProductApi
    {
        // 1. 获取分类树
        [Get("/api/product-categories/tree")]
        Task<ServiceResult<List<ProductCategoryDto>>> GetCategoryTreeAsync();

        // 2. 搜索产品 (分页)
        // Refit 会把 SearchDto 里的属性拼接到 URL 参数中
        [Get("/api/products")]
        Task<ServiceResult<PagedResultDto<ProductDto>>> GetProductsAsync([Query] ProductSearchDto search);

        // 3. 获取单个详情
        [Get("/api/Product/{id}")]
        Task<ServiceResult<ProductDto>> GetByIdAsync(int id);
    }
}