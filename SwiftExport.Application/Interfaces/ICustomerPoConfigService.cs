using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.DTOs.Customers.Requests;
using SwiftExport.Contracts.DTOs.Customers.Responses;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Relations;
namespace SwiftExport.Application.Interfaces;

public partial interface ICustomerPoConfigService : IBaseService<CustomerPoConfig, CustomerPoConfigDto>
{
    Task<ServiceResult<PagedResponseBase<CustomerPoConfigDto>>> GetPagedAsync(CustomerPoConfigSearchRequest request);
    Task<ServiceResult<IReadOnlyList<CustomerPoConfigDto>>> GetAllAsync();
    Task<ServiceResult<CustomerPoConfigBatchSaveResponse>> BatchSaveAsync(CustomerPoConfigBatchSaveRequest batchReq);

}
