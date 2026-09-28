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
        // 1. 获取分类树
        [Get("/product-categories/tree")]
        Task<ServiceResult<List<ProductCategoryDto>>> GetCategoryTreeAsync();

        // 2. 搜索产品 (分页)
        // Refit 会把 SearchDto 序列化为 JSON 放到 Body 中传递，避开了复杂对象的 URL 序列化问题
        [Post("/page")]
        Task<ServiceResult<PagedResponseBase<ProductDto>>> GetPagedAsync([Body] ProductSearchRequest request);

        [Post("/lookup")]
        Task<ServiceResult<List<ProductLookupDto>>> SearchLookupAsync([Body] ProductLookupRequest request);

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
