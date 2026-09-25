using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExport.Contracts.DTOs.Suppliers.Requests;
using SwiftExport.Contracts.DTOs.Suppliers.Responses;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces.Suppliers.SupplierProducts.Maintenance;

/// <summary>维护供应商自有产品资料；公司 Product 关联可选。</summary>
public interface ISupplierProductMaintenanceService
{
    /// <summary>新增供应商产品资料。</summary>
    Task<ServiceResult<SaveItemResult>> AddAsync(CreateItemRequest<SupplierProductDto> item);
    /// <summary>按版本更新供应商产品资料。</summary>
    Task<ServiceResult<SaveItemResult>> UpdateAsync(UpdateItemRequest<SupplierProductDto> item);
    /// <summary>按版本删除供应商产品资料。</summary>
    Task<ServiceResult<SaveItemResult>> DeleteAsync(DeleteItemRequest item);
    /// <summary>原子保存供应商产品变更集。</summary>
    Task<ServiceResult<SupplierProductBatchSaveResponse>> BatchSaveAsync(SupplierProductBatchSaveRequest request);
}
