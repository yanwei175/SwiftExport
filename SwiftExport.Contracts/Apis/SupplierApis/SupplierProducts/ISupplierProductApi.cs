using Refit;
using SwiftExport.Contracts.Apis.BaseApi;
using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExport.Contracts.DTOs.Suppliers.Requests;
using SwiftExport.Contracts.DTOs.Suppliers.Responses;
using SwiftExport.Contracts.Wrappers;
using System.Threading.Tasks;
using System.Threading;

namespace SwiftExport.Contracts.Apis.SupplierApis
{
    /// <summary>提供供应商产品资料查询、批量维护和默认供应商设置契约。</summary>
    public interface ISupplierProductApi : IGenericApi<SupplierProductDto>
    {
        /// <summary>按供应商和搜索条件分页读取资料，允许页面切换或释放时取消请求。</summary>
        [Post("/page")]
        Task<ServiceResult<PagedResponseBase<SupplierProductDto>>> GetPagedAsync([Body] SupplierProductSearchRequest request, CancellationToken cancellationToken = default);

        [Post("/batch-save")]
        Task<ServiceResult<SupplierProductBatchSaveResponse>> BatchSaveAsync([Body] SupplierProductBatchSaveRequest request);

        [Post("/set-product-default-supplier")]
        Task<ServiceResult<SetProductDefaultSupplierResult>> SetProductDefaultSupplierAsync(
            [Body] SetProductDefaultSupplierCommand command);
    }
}
