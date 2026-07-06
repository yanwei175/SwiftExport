using Refit;
using SwiftExport.Contracts.Apis.BaseApi;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.Wrappers;
using System.Threading.Tasks;

namespace SwiftExport.Contracts.Apis.CustomerApis
{
    public interface ICustomerPoConfigApi : IGenericApi<CustomerPoConfigDto>
    {
        [Get("/page")]
        Task<ServiceResult<PagedResultDto<CustomerPoConfigDto>>> GetPagedAsync([Query] PagedRequestDto request);
    }
}
