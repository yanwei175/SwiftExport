using Refit;
using SwiftExport.Contracts.Apis.BaseApi;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.Wrappers;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace SwiftExport.Contracts.Apis.CustomerApis
{
    public interface ICustomerProductApi : IGenericApi<CustomerProductDto>
    {
        [Post("/search")]
        Task<ServiceResult<PagedResultDto<CustomerProductDto>>> GetCustomerProductPagedListAsync([Body] ProductSearchDto search);

        [Get("/haveproductCustomers")]
        Task<ServiceResult<List<CustomerDto>>> GetAllHaveProductCustomerList();

        [Get("/customersku/{sku}")]
        Task<ServiceResult<CustomerProductDto>> GetByCustomerSku(int customerId, string sku);
    }
}
