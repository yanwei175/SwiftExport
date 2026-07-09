using Refit;
using SwiftExport.Contracts.Apis.BaseApi;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.DTOs.Customers.Requests;
using SwiftExport.Contracts.DTOs.Customers.Responses;
using SwiftExport.Contracts.Wrappers;
using System.Threading.Tasks;
namespace SwiftExport.Contracts.Apis.CustomerApis
{
    public interface ICustomerPoConfigApi : IGenericApi<CustomerPoConfigDto>
    {
        [Post("/page")]
        Task<ServiceResult<PagedResponseBase<CustomerPoConfigDto>>> GetPagedAsync([Body] CustomerPoConfigSearchRequest request);

        [Post("/batch-save")]
        Task<ServiceResult<CustomerPoConfigBatchSaveResponse>> BatchSaveAsync([Body] CustomerPoConfigBatchSaveRequest request);
    }
}
