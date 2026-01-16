using SwiftExport.Contracts.DTOs.Product;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;

namespace SwiftExport.Application.Interfaces;

// 继承泛型接口，获得 GetAll, GetById, Create, Update, Delete 五大神技
public interface IProductService : IBaseService<Product, ProductDto>
{
    // 在这里扩展特有的业务方法
    Task<ServiceResult<ProductDto>> GetBySkuAsync(string sku);
}