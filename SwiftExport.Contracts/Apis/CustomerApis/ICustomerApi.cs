using Refit;
using SwiftExport.Contracts.Apis.BaseApi;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.Wrappers;
using System.Threading.Tasks;

using SwiftExport.Contracts.DTOs.Customers.Requests;
using SwiftExport.Contracts.DTOs.Customers.Responses;
namespace SwiftExport.Contracts.Apis.CustomerApis
{
    public interface ICustomerApi : IGenericApi<CustomerDto>
    {
        [Get("/page")]
        Task<ServiceResult<PagedResultDto<CustomerDto>>> GetPagedAsync([Query] CustomerSearchRequest request);

        [Post("/batch-save")]
        Task<ServiceResult<CustomerBatchSaveResponse>> BatchSaveAsync([Body] CustomerBatchSaveRequest request);

    }
}
