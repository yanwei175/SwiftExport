using Refit;
using SwiftExport.Contracts.Apis.BaseApi;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.DTOs.Customers.Requests;
using SwiftExport.Contracts.DTOs.Customers.Responses;
using SwiftExport.Contracts.Wrappers;
using System.Threading.Tasks;

namespace SwiftExport.Contracts.Apis.CustomerApis
{
    public interface ICustomerApi : IGenericApi<CustomerDto>
    {
        [Post("/page")]
        Task<ServiceResult<PagedResponseBase<CustomerDto>>> GetPagedAsync([Body] CustomerSearchRequest request);

        [Post("/batch-save")]
        Task<ServiceResult<CustomerBatchSaveResponse>> BatchSaveAsync([Body] CustomerBatchSaveRequest request);

    }
}
