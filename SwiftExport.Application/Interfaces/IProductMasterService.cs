using SwiftExport.Contracts.DTOs.Products.ProductMasters;
using SwiftExport.Contracts.DTOs.Products.ProductMasters.Requests;
using SwiftExport.Contracts.DTOs.Products.ProductMasters.Responses;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces;

/// <summary>主产品应用服务边界，负责主型号快速编辑和主从聚合事务。</summary>
public interface IProductMasterService
{
    /// <summary>仅保存指定主型号的中性产品变更，不更新父记录字段及版本。</summary>
    Task<ServiceResult<ProductMasterDetailDto>> SaveNeutralsAsync(Guid productMasterId, BatchSaveRequestBase<ProductNeutralWriteDto> request, CancellationToken cancellationToken = default);

    /// <summary>分页查询主型号。</summary>
    Task<ServiceResult<PagedResponseBase<ProductMasterListItemDto>>> GetPagedAsync(ProductMasterSearchRequest request, CancellationToken cancellationToken = default);

    /// <summary>批量设置主型号分类；任一项失败时整批回滚。</summary>
    Task<ServiceResult<AssignProductMastersToCategoryResult>> AssignCategoryAsync(AssignProductMastersToCategoryRequest request, CancellationToken cancellationToken = default);

    /// <summary>读取主型号及其中性产品明细。</summary>
    Task<ServiceResult<ProductMasterDetailDto>> GetDetailAsync(Guid productMasterId, CancellationToken cancellationToken = default);

    /// <summary>原子保存管理页主型号变更。</summary>
    Task<ServiceResult<ProductMasterBatchSaveResponse>> BatchSaveAsync(ProductMasterBatchSaveRequest request, CancellationToken cancellationToken = default);

    /// <summary>原子保存主型号头部和全部中性产品变更。</summary>
    Task<ServiceResult<SaveProductMasterDetailResponse>> SaveDetailAsync(SaveProductMasterDetailRequest request, CancellationToken cancellationToken = default);
}
