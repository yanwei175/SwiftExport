using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.DTOs.Customers.Requests;
using SwiftExport.Contracts.DTOs.Customers.Responses;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;
namespace SwiftExport.Application.Interfaces;

public partial interface ICustomerService : IBaseService<Customer, CustomerDto>
{

    Task<ServiceResult<PagedResponseBase<CustomerDto>>> GetPagedAsync(CustomerSearchRequest request);
    Task<ServiceResult<CustomerBatchSaveResponse>> BatchSaveAsync(CustomerBatchSaveRequest batchReq);


}
