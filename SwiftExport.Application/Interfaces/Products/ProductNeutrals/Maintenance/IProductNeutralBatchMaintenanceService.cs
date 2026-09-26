using SwiftExport.Contracts.DTOs.Products.ProductNeutralManager.Requests;
using SwiftExport.Contracts.DTOs.Products.ProductNeutralManager.Responses;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces.Products.ProductNeutrals.Maintenance;

/// <summary>原子维护跨主型号中性产品列表。</summary>
public interface IProductNeutralBatchMaintenanceService
{
    /// <summary>原子保存新增、修改、重新归属和软删除。</summary>
    Task<ServiceResult<ProductNeutralManagerBatchSaveResponse>> BatchSaveAsync(ProductNeutralManagerBatchSaveRequest request, CancellationToken cancellationToken = default);
}
