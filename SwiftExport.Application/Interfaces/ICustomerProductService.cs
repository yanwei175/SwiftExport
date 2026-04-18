using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Entities.Relations;

namespace SwiftExport.Application.Interfaces;

public interface ICustomerProductService : IBaseService<Core.Entities.Relations.CustomerProduct, CustomerProductDto>
{
    Task<ServiceResult<IReadOnlyList<CustomerDto>>> GetAllHaveProductCustomerListAsync();

    // 在这里扩展特有的业务方法
    Task<ServiceResult<CustomerProductDto>> GetByCustomerSkuAsync(string customerSku, int customerId);

    Task<ServiceResult<PagedResultDto<CustomerProductDto>>> GetPagedListAsync(ProductSearchDto search);
}