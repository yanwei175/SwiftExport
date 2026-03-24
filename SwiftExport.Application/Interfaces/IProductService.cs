using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;

namespace SwiftExport.Application.Interfaces;

public interface IProductService : IBaseService<Product, ProductDto>
{
    Task<ServiceResult<IReadOnlyList<string>>> GetAllCustomerCodeListAsync();

    // 在这里扩展特有的业务方法
    Task<ServiceResult<ProductDto>> GetBySkuAsync(string sku);

    Task<ServiceResult<PagedResultDto<ProductDto>>> GetPagedListAsync(ProductSearchDto search);
}