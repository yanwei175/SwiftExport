using Refit;
using SwiftExport.Contracts.Apis.BaseApi;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.Wrappers;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SwiftExport.Contracts.Apis.CustomerApis
{
    public interface ICustomerProductApi : IGenericApi<CustomerProductDto>
    {
        [Get("/page")]
        Task<ServiceResult<PagedResultDto<CustomerProductDto>>> GetPagedAsync([Query] PagedRequestDto request);

        [Post("/search")]
        Task<ServiceResult<PagedResultDto<CustomerProductDto>>> GetCustomerProductPagedListAsync([Body] ProductSearchRequest search);

        [Get("/haveproductCustomers")]
        Task<ServiceResult<List<CustomerDto>>> GetAllHaveProductCustomerList();

        [Get("/customersku/{sku}")]
        Task<ServiceResult<CustomerProductDto>> GetByCustomerSku(int customerId, string sku);
    }
}
