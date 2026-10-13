using System;
using System.Threading;
using System.Threading.Tasks;
using Refit;
using SwiftExport.Contracts.DTOs.Products.Attributes;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Contracts.Apis.ProductApis
{
    /// <summary>共享标签定义读取与原子维护 API；基地址对应 api/ProductAttribute。</summary>
    public interface IProductAttributeApi
    {
        /// <summary>读取全部有效定义，供登录会话内缓存使用。</summary>
        [Get("/definitions")]
        Task<ServiceResult<ProductAttributeDefinitionsDto>> GetDefinitionsAsync(CancellationToken cancellationToken = default(CancellationToken));
        /// <summary>读取一个分类及祖先提供的定义、版本和直接关联来源。</summary>
        [Get("/definitions/{categoryId}")]
        Task<ServiceResult<ProductAttributeDefinitionsDto>> GetCategoryDefinitionsAsync(Guid categoryId, CancellationToken cancellationToken = default(CancellationToken));
        /// <summary>原子保存全局组、选项与分类直接关联变更。</summary>
        [Post("/definitions/batch-save")]
        Task<ServiceResult<SaveProductAttributeDefinitionsResponse>> BatchSaveDefinitionsAsync([Body] SaveProductAttributeDefinitionsRequest request, CancellationToken cancellationToken = default(CancellationToken));
        /// <summary>旧分类维护入口，仅保留升级提示契约，不再接受定义修改。</summary>
        [Post("/definitions/{categoryId}/batch-save")]
        Task<ServiceResult<SaveProductAttributeDefinitionsResponse>> BatchSaveAsync(Guid categoryId, [Body] SaveProductAttributeDefinitionsRequest request, CancellationToken cancellationToken = default(CancellationToken));
    }
}
