using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.DTOs.Customers.Requests;
using SwiftExport.Contracts.DTOs.Customers.Responses;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Relations;
namespace SwiftExport.Application.Interfaces.Customers.PoConfigs;

public partial interface ICustomerPoConfigService : IBaseService<CustomerPoConfig, CustomerPoConfigDto>
{
    /// <summary>按客户与供应商标识查询完整的只读采购默认配置。</summary>
    Task<ServiceResult<CustomerPoConfigDto>> GetByPartiesAsync(Guid customerId, Guid supplierId, CancellationToken cancellationToken = default);

    Task<ServiceResult<PagedResponseBase<CustomerPoConfigDto>>> GetPagedAsync(CustomerPoConfigSearchRequest request);
    Task<ServiceResult<IReadOnlyList<CustomerPoConfigDto>>> GetAllAsync();
    Task<ServiceResult<CustomerPoConfigBatchSaveResponse>> BatchSaveAsync(CustomerPoConfigBatchSaveRequest batchReq);

}
