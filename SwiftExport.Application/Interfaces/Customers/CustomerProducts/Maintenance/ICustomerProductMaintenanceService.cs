using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.DTOs.Customers.Requests;
using SwiftExport.Contracts.DTOs.Customers.Responses;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces.Customers.CustomerProducts.Maintenance;

/// <summary>维护客户自有产品资料；公司 Product 关联可选。</summary>
public interface ICustomerProductMaintenanceService
{
    /// <summary>新增客户产品资料。</summary>
    Task<ServiceResult<SaveItemResult>> AddAsync(CreateItemRequest<CustomerProductDto> item);
    /// <summary>按版本更新客户产品资料。</summary>
    Task<ServiceResult<SaveItemResult>> UpdateAsync(UpdateItemRequest<CustomerProductDto> item);
    /// <summary>按版本删除客户产品资料。</summary>
    Task<ServiceResult<SaveItemResult>> DeleteAsync(DeleteItemRequest item);
    /// <summary>原子保存客户产品变更集。</summary>
    Task<ServiceResult<CustomerProductBatchSaveResponse>> BatchSaveAsync(CustomerProductBatchSaveRequest request);
}
