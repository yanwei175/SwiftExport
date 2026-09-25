using SwiftExport.Contracts.DTOs.Products.ProductMasters.Requests;
using SwiftExport.Contracts.DTOs.Products.ProductMasters.Responses;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces.Products.ProductMasters.Maintenance;

/// <summary>维护主型号并原子保存显式请求的 Product 同步。</summary>
public interface IProductMasterMaintenanceService
{
    /// <summary>原子设置主型号分类。</summary>
    Task<ServiceResult<AssignProductMastersToCategoryResult>> AssignCategoryAsync(AssignProductMastersToCategoryRequest request, CancellationToken cancellationToken = default);
    /// <summary>原子保存主型号管理页变更。</summary>
    Task<ServiceResult<ProductMasterBatchSaveResponse>> BatchSaveAsync(ProductMasterBatchSaveRequest request, CancellationToken cancellationToken = default);
    /// <summary>原子保存主型号及中性产品变更。</summary>
    Task<ServiceResult<SaveProductMasterDetailResponse>> SaveDetailAsync(SaveProductMasterDetailRequest request, CancellationToken cancellationToken = default);
}
