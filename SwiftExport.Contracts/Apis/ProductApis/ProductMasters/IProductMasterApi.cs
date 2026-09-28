using Refit;
using SwiftExport.Contracts.DTOs.Products.ProductMasters;
using SwiftExport.Contracts.DTOs.Products.ProductMasters.Requests;
using SwiftExport.Contracts.DTOs.Products.ProductMasters.Responses;
using SwiftExport.Contracts.Wrappers;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace SwiftExport.Contracts.Apis.ProductApis
{
    /// <summary>主产品资料管理 API，负责主型号查询、主从保存和已保存资料的独立下发。</summary>
    public interface IProductMasterApi
    {
        /// <summary>分页查询主型号。</summary>
        [Post("/page")]
        Task<ServiceResult<PagedResponseBase<ProductMasterListItemDto>>> GetPagedAsync(
            [Body] ProductMasterSearchRequest request,
            CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>批量设置主型号分类。</summary>
        [Post("/assign-category")]
        Task<ServiceResult<AssignProductMastersToCategoryResult>> AssignCategoryAsync(
            [Body] AssignProductMastersToCategoryRequest request,
            CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>读取一个主型号及其中性产品明细。</summary>
        [Get("/{productMasterId}")]
        Task<ServiceResult<ProductMasterDetailDto>> GetDetailAsync(
            Guid productMasterId,
            CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>提交管理页主型号变更和本批次物料同步选项，返回逐行保存结果。</summary>
        [Post("/batch-save")]
        Task<ServiceResult<ProductMasterBatchSaveResponse>> BatchSaveAsync(
            [Body] ProductMasterBatchSaveRequest request,
            CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>原子保存主型号及其中性产品明细。</summary>
        [Post("/save-detail")]
        Task<ServiceResult<SaveProductMasterDetailResponse>> SaveDetailAsync(
            [Body] SaveProductMasterDetailRequest request,
            CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>下发中英文描述。</summary>
        [Post("/sync-descriptions")]
        Task<ServiceResult<SyncProductMasterDataResponse>> SyncDescriptionsAsync([Body] SyncProductMasterDataRequest request, CancellationToken cancellationToken = default(CancellationToken));
        /// <summary>下发中英文配置。</summary>
        [Post("/sync-features")]
        Task<ServiceResult<SyncProductMasterDataResponse>> SyncFeaturesAsync([Body] SyncProductMasterDataRequest request, CancellationToken cancellationToken = default(CancellationToken));
        /// <summary>下发默认供应商。</summary>
        [Post("/sync-supplier")]
        Task<ServiceResult<SyncProductMasterDataResponse>> SyncSupplierAsync([Body] SyncProductMasterDataRequest request, CancellationToken cancellationToken = default(CancellationToken));
        /// <summary>直接下发毛重和体积到产品单位字段。</summary>
        [Post("/sync-weight-volume")]
        Task<ServiceResult<SyncProductMasterDataResponse>> SyncWeightVolumeAsync([Body] SyncProductMasterDataRequest request, CancellationToken cancellationToken = default(CancellationToken));
        /// <summary>同时下发四组产品资料。</summary>
        [Post("/sync-all")]
        Task<ServiceResult<SyncProductMasterDataResponse>> SyncAllAsync([Body] SyncProductMasterDataRequest request, CancellationToken cancellationToken = default(CancellationToken));
    }
}
