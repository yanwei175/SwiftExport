using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.DTOs.Products.Responses;
using SwiftExport.Contracts.DTOs.Products.ProductMasters.Requests;
using SwiftExport.Contracts.DTOs.Products.ProductMasters.Responses;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.Products.Catalog;

namespace SwiftExport.Application.Interfaces.Products.ProductMasters.Maintenance;

/// <summary>维护主型号聚合，并供物料按明确选集主动获取主型号资料。</summary>
public interface IProductMasterMaintenanceService
{
    /// <summary>原子设置主型号分类。</summary>
    Task<ServiceResult<AssignProductMastersToCategoryResult>> AssignCategoryAsync(AssignProductMastersToCategoryRequest request, CancellationToken cancellationToken = default);
    /// <summary>原子保存主型号管理页变更。</summary>
    Task<ServiceResult<ProductMasterBatchSaveResponse>> BatchSaveAsync(ProductMasterBatchSaveRequest request, CancellationToken cancellationToken = default);
    /// <summary>原子保存主型号及中性产品变更。</summary>
    Task<ServiceResult<SaveProductMasterDetailResponse>> SaveDetailAsync(SaveProductMasterDetailRequest request, CancellationToken cancellationToken = default);
    /// <summary>按服务端指定的资料组，把有效主型号现值拉取到所选关联产品，报告跳过和更新数量。</summary>
    Task<ServiceResult<PullProductMasterDataResponse>> PullProductsAsync(PullProductMasterDataRequest request, ProductMasterSyncFields fields, CancellationToken cancellationToken = default);
}
