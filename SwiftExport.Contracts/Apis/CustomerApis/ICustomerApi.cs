using Refit;
using SwiftExport.Contracts.Apis.BaseApi;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.Wrappers;
using System.Threading.Tasks;

namespace SwiftExport.Contracts.Apis.CustomerApis
{
    public interface ICustomerApi : IGenericApi<CustomerDto>
    {
        [Get("/page")]
        Task<ServiceResult<PagedResultDto<CustomerDto>>> GetPagedAsync([Query] PagedRequestDto request);

        [Post("/batch-save")]
        Task<ServiceResult<BatchResultDto>> BatchSaveAsync([Body] BatchRequestDto<CustomerDto> request);

    }
}
