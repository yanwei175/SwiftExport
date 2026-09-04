using Refit;
using SwiftExport.Contracts.DTOs.Products.ProductMasters;
using SwiftExport.Contracts.Wrappers;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace SwiftExport.Contracts.Apis.ProductApis
{
    /// <summary>中性产品独立保存接口，不更新主型号头部。</summary>
    public interface IProductNeutralApi
    {
        /// <summary>原子保存指定主型号的中性产品变更并返回最新详情。</summary>
        [Post("/{productMasterId}/batch-save")]
        Task<ServiceResult<ProductMasterDetailDto>> BatchSaveAsync(
            Guid productMasterId,
            [Body] BatchSaveRequestBase<ProductNeutralWriteDto> request,
            CancellationToken cancellationToken = default(CancellationToken));
    }
}
