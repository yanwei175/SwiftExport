using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.DTOs.Customers.Requests;
using SwiftExport.Contracts.DTOs.Customers.Responses;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;
namespace SwiftExport.Application.Interfaces.Customers.Management;

/// <summary>客户管理读取、批量维护及既有 CRUD 用例边界。</summary>
public partial interface ICustomerService : IBaseService<Customer, CustomerDto>
{

    /// <summary>读取全部未删除客户管理资料，保留停用客户及编辑所需版本。</summary>
    Task<ServiceResult<IReadOnlyList<CustomerDto>>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<ServiceResult<PagedResponseBase<CustomerDto>>> GetPagedAsync(CustomerSearchRequest request);
    Task<ServiceResult<CustomerBatchSaveResponse>> BatchSaveAsync(CustomerBatchSaveRequest batchReq);


}
