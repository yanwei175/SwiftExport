using AutoMapper;
using SwiftExport.Application.Interfaces;
using SwiftExport.Application.Services.Base;
using SwiftExport.Contracts.DTOs.Product;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces;

namespace SwiftExport.Application.Services.Products;

public class ProductService(IRepository<Product> repository, IMapper mapper) : BaseService<Product, ProductDto>(repository, mapper), IProductService
{

    // 实现特有的业务逻辑：根据 SKU 查询
    public async Task<ServiceResult<ProductDto>> GetBySkuAsync(string sku)
    {
        // 注意：这里的 WHERE Sku = @Sku 对应数据库字段
        var list = await _repository.GetListAsync("WHERE Sku = @Sku", new { Sku = sku });
        if (list == null)
            return ServiceResult<ProductDto>.Fail($"未找到 SKU 为 {sku} 的产品");

        // 转 DTO
        var dto = _mapper.Map<ProductDto>(list);
        return ServiceResult<ProductDto>.Ok(dto);
    }

}