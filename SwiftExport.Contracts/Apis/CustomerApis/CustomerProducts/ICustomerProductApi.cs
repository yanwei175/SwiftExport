using Refit;
using SwiftExport.Contracts.Apis.BaseApi;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.DTOs.Customers.Requests;
using SwiftExport.Contracts.DTOs.Customers.Responses;
using SwiftExport.Contracts.Wrappers;
using System.Collections.Generic;
using System;
using System.Threading.Tasks;
namespace SwiftExport.Contracts.Apis.CustomerApis
{
    public interface ICustomerProductApi : IGenericApi<CustomerProductDto>
    {
        [Post("/page")]
        Task<ServiceResult<PagedResponseBase<CustomerProductDto>>> GetPagedAsync([Body] CustomerProductSearchRequest request);

        [Get("/haveproductCustomers")]
        Task<ServiceResult<List<CustomerDto>>> GetAllHaveProductCustomerList();

        [Get("/customersku/{sku}")]
        Task<ServiceResult<CustomerProductDto>> GetByCustomerSku(Guid customerId, string sku);

        [Post("/batch-save")]
        Task<ServiceResult<CustomerProductBatchSaveResponse>> BatchSaveAsync([Body] CustomerProductBatchSaveRequest request);


    }
}
