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
        /// <summary>读取一个属性模板直接提供的定义、版本及组关联。</summary>
        [Get("/definitions/profiles/{profileId}")]
        Task<ServiceResult<ProductAttributeDefinitionsDto>> GetProfileDefinitionsAsync(Guid profileId, CancellationToken cancellationToken = default(CancellationToken));
        /// <summary>原子保存全局组、选项与模板直接关联变更。</summary>
        [Post("/definitions/batch-save")]
        Task<ServiceResult<SaveProductAttributeDefinitionsResponse>> BatchSaveDefinitionsAsync([Body] SaveProductAttributeDefinitionsRequest request, CancellationToken cancellationToken = default(CancellationToken));
    }
}
