using Refit;
using System.Collections.Generic;
using SwiftExport.Contracts.DTOs.Products.ProductNeutralManager.Import;
using SwiftExport.Contracts.DTOs.Products.ProductNeutralManager;
using SwiftExport.Contracts.DTOs.Products.ProductNeutralManager.Requests;
using SwiftExport.Contracts.DTOs.Products.ProductNeutralManager.Responses;
using SwiftExport.Contracts.Wrappers;
using System.Threading;
using System.Threading.Tasks;

namespace SwiftExport.Contracts.Apis.ProductApis
{
    /// <summary>跨主型号中性产品管理 API。</summary>
    public interface IProductNeutralManagerApi
    {
        /// <summary>活动管理员按行导入一至一百条中性资料，不改变编码所属父级。</summary>
        [Post("/import")]
        Task<ServiceResult<BatchSaveResponseBase>> ImportAsync([Body] List<ProductNeutralImportRowDto> rows,
            CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>按关键词、主型号分类和颜色分页查询中性产品。</summary>
        [Post("/page")]
        Task<ServiceResult<PagedResponseBase<ProductNeutralManagerListItemDto>>> GetPagedAsync(
            [Body] ProductNeutralManagerSearchRequest request,
            CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>原子保存中性产品新增、修改、重新归属和软删除变更。</summary>
        [Post("/batch-save")]
        Task<ServiceResult<ProductNeutralManagerBatchSaveResponse>> BatchSaveAsync(
            [Body] ProductNeutralManagerBatchSaveRequest request,
            CancellationToken cancellationToken = default(CancellationToken));
    }
}
