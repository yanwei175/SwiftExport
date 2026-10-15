using Refit;
using SwiftExport.Contracts.Apis.BaseApi;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.DTOs.Products.Responses;
using SwiftExport.Contracts.Wrappers;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
namespace SwiftExport.Contracts.Apis.ProductApis
{
    public interface IProductApi : IGenericApi<ProductDto>
    {
        /// <summary>读取物料编辑详情和自身标签，不修改资料。</summary>
        [Get("/{id}/detail")]
        Task<ServiceResult<ProductDetailDto>> GetDetailAsync(System.Guid id, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>按物料版本原子保存可写资料与自身标签。</summary>
        [Post("/{id}/detail")]
        Task<ServiceResult<SaveProductDetailResponse>> SaveDetailAsync(System.Guid id, [Body] SaveProductDetailRequest request, CancellationToken cancellationToken = default(CancellationToken));

        // 1. 获取分类树
        [Get("/product-categories/tree")]
        Task<ServiceResult<List<ProductCategoryDto>>> GetCategoryTreeAsync();

        /// <summary>按物料筛选分页读取资料，取消令牌贯穿 HTTP 与数据库读取，不改变已有默认筛选。</summary>
        [Post("/page")]
        Task<ServiceResult<PagedResponseBase<ProductDto>>> GetPagedAsync([Body] ProductSearchRequest request, CancellationToken cancellationToken = default(CancellationToken));

        [Post("/lookup")]
        Task<ServiceResult<List<ProductLookupDto>>> SearchLookupAsync([Body] ProductLookupRequest request, CancellationToken cancellationToken = default(CancellationToken));

        // 3. 获取单个详情
        [Get("/customerCodes")]
        Task<ServiceResult<List<string>>> GetAllCustomerCodeListAsync();

        [Get("/sku/{sku}")]
        Task<ServiceResult<ProductDto>> GetBySkuAsync(string sku);

        [Post("/batch-save")]
        Task<ServiceResult<ProductBatchSaveResponse>> BatchSaveAsync([Body] ProductBatchSaveRequest request);

        /// <summary>从所选产品的主型号拉取中英文描述。</summary>
        [Post("/pull-master-descriptions")]
        Task<ServiceResult<PullProductMasterDataResponse>> PullMasterDescriptionsAsync([Body] PullProductMasterDataRequest request, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>从所选产品的主型号拉取中英文配置。</summary>
        [Post("/pull-master-features")]
        Task<ServiceResult<PullProductMasterDataResponse>> PullMasterFeaturesAsync([Body] PullProductMasterDataRequest request, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>从所选产品的主型号拉取默认供应商。</summary>
        [Post("/pull-master-supplier")]
        Task<ServiceResult<PullProductMasterDataResponse>> PullMasterSupplierAsync([Body] PullProductMasterDataRequest request, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>从所选产品的主型号拉取毛重和体积。</summary>
        [Post("/pull-master-weight-volume")]
        Task<ServiceResult<PullProductMasterDataResponse>> PullMasterWeightVolumeAsync([Body] PullProductMasterDataRequest request, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>从所选产品的主型号拉取全部四组资料。</summary>
        [Post("/pull-master-all")]
        Task<ServiceResult<PullProductMasterDataResponse>> PullMasterAllAsync([Body] PullProductMasterDataRequest request, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>读取产品资料维护可用的活动仓库简要候选。</summary>
        [Get("/default-warehouse-candidates")]
        Task<ServiceResult<PagedResponseBase<ProductDefaultWarehouseCandidateDto>>> GetDefaultWarehouseCandidatesAsync(
            [Query] PagedRequestBase request, CancellationToken cancellationToken = default(CancellationToken));

    }
}
