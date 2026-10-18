using Refit;
using SwiftExport.Contracts.Apis.BaseApi;
using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExport.Contracts.DTOs.Suppliers.Requests;
using SwiftExport.Contracts.DTOs.Suppliers.Responses;
using SwiftExport.Contracts.Wrappers;
using System.Threading.Tasks;
using System.Threading;
using System.Collections.Generic;
namespace SwiftExport.Contracts.Apis.SupplierApis
{
    /// <summary>提供供应商管理资料的完整快照、分页读取和批量保存契约。</summary>
    public interface ISupplierApi : IGenericApi<SupplierDto>
    {
        /// <summary>读取全部未删除供应商及并发版本，供管理页本地筛选和编辑；支持取消读取。</summary>
        [Get("/all")]
        Task<ServiceResult<IReadOnlyList<SupplierDto>>> GetAllAsync(CancellationToken cancellationToken = default);

        [Post("/page")]
        Task<ServiceResult<PagedResponseBase<SupplierDto>>> GetPagedAsync([Body] SupplierSearchRequest request);

        [Post("/batch-save")]
        Task<ServiceResult<SupplierBatchSaveResponse>> BatchSaveAsync([Body] SupplierBatchSaveRequest request);
    }
}
