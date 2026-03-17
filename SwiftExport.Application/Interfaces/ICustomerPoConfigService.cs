using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Relations;

namespace SwiftExport.Application.Interfaces
{
    public interface ICustomerPoConfigService : IBaseService<CustomerPoConfig, CustomerPoConfigDto>
    {
        Task<ServiceResult<BatchResultDto>> BatchSaveAsync(BatchRequestGeneric<CustomerPoConfigDto> batchReq);
    }
}
