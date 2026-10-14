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
    /// <summary>主产品资料管理 API，负责主型号查询及主从资料保存。</summary>
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

        /// <summary>提交管理页主型号变更，返回逐行保存结果。</summary>
        [Post("/batch-save")]
        Task<ServiceResult<ProductMasterBatchSaveResponse>> BatchSaveAsync(
            [Body] ProductMasterBatchSaveRequest request,
            CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>原子保存主型号及其中性产品明细。</summary>
        [Post("/save-detail")]
        Task<ServiceResult<SaveProductMasterDetailResponse>> SaveDetailAsync(
            [Body] SaveProductMasterDetailRequest request,
            CancellationToken cancellationToken = default(CancellationToken));

    }
}
