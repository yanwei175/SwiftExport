using Refit;
using SwiftExport.Contracts.Apis.BaseApi;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.DTOs.Products.Responses;
using SwiftExport.Contracts.Wrappers;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;

namespace SwiftExport.Contracts.Apis.ProductApis
{
    /// <summary>共享 BOM 的组成读写、管理成品分页和编号预览契约。</summary>
    public interface IBomApi : IGenericApi<BomDto>
    {
        /// <summary>按 BOM 管理条件查询成品，复用平台分页包装。</summary>
        [Post("/products/page")]
        Task<ServiceResult<PagedResponseBase<BomProductListItemDto>>> SearchProductsAsync([Body] BomProductSearchRequest request, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>读取共享编号的数量和既有存储情况，结果不预占编号。</summary>
        [Get("/number-info")]
        Task<ServiceResult<BomNumberInfoDto>> GetNumberInfoAsync([Query] string bomNo, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>读取已保存 BOM 多层结构，页面关闭时可取消。</summary>
        [Get("/tree/{productSku}")]
        Task<ServiceResult<List<BomTreeDto>>> GetBomTreeAsync(string productSku, CancellationToken cancellationToken = default(CancellationToken));
        /// <summary>读取当前共享组成，建立绑定后尚无组成时返回成功空集合。</summary>
        [Get("/by-no/{bomNo}")]
        Task<ServiceResult<List<BomDto>>> GetBomByBomNo(string bomNo, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>校验已保存结构，不接受客户端未保存编辑。</summary>
        [Get("/validate/{productSku}")]
        Task<ServiceResult<bool>> ValidateBomAsync(string productSku, CancellationToken cancellationToken = default(CancellationToken));

        [Post("/batch-save")]
        Task<ServiceResult<BomBatchSaveResponse>> BatchSaveAsync([Body] BomBatchSaveRequest request);
    }
}
