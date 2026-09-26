using SwiftExport.Contracts.DTOs.Products.ProductNeutralManager;
using SwiftExport.Contracts.DTOs.Products.ProductNeutralManager.Requests;
using SwiftExport.Contracts.DTOs.Products.ProductNeutralManager.Responses;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces;

/// <summary>跨主型号中性产品管理应用服务边界。</summary>
public interface IProductNeutralManagerService
{
    /// <summary>分页查询中性产品管理投影。</summary>
    Task<ServiceResult<PagedResponseBase<ProductNeutralManagerListItemDto>>> GetPagedAsync(
        ProductNeutralManagerSearchRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>在单一事务内批量保存新增、修改、重新归属和软删除变更。</summary>
    Task<ServiceResult<ProductNeutralManagerBatchSaveResponse>> BatchSaveAsync(
        ProductNeutralManagerBatchSaveRequest request,
        CancellationToken cancellationToken = default);
}
