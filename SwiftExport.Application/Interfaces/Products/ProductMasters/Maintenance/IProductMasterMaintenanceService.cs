using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.DTOs.Products.Responses;
using SwiftExport.Contracts.DTOs.Products.ProductMasters.Requests;
using SwiftExport.Contracts.DTOs.Products.ProductMasters.Responses;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.Products.Catalog;

namespace SwiftExport.Application.Interfaces.Products.ProductMasters.Maintenance;

/// <summary>维护主型号，并在事务中执行保存时同步或已保存资料的独立下发。</summary>
public interface IProductMasterMaintenanceService
{
    /// <summary>原子设置主型号分类。</summary>
    Task<ServiceResult<AssignProductMastersToCategoryResult>> AssignCategoryAsync(AssignProductMastersToCategoryRequest request, CancellationToken cancellationToken = default);
    /// <summary>原子保存主型号管理页变更，并按请求选项同步新增、修改项关联的物料。</summary>
    Task<ServiceResult<ProductMasterBatchSaveResponse>> BatchSaveAsync(ProductMasterBatchSaveRequest request, CancellationToken cancellationToken = default);
    /// <summary>原子保存主型号及中性产品变更。</summary>
    Task<ServiceResult<SaveProductMasterDetailResponse>> SaveDetailAsync(SaveProductMasterDetailRequest request, CancellationToken cancellationToken = default);
    /// <summary>按服务端选定的资料组原子下发数据库当前主型号值，目标产品由现有关联决定。</summary>
    Task<ServiceResult<SyncProductMasterDataResponse>> SyncProductsAsync(SyncProductMasterDataRequest request, ProductMasterSyncFields fields, CancellationToken cancellationToken = default);
    /// <summary>按服务端指定的资料组，把有效主型号现值拉取到所选关联产品，报告跳过和更新数量。</summary>
    Task<ServiceResult<PullProductMasterDataResponse>> PullProductsAsync(PullProductMasterDataRequest request, ProductMasterSyncFields fields, CancellationToken cancellationToken = default);
}
