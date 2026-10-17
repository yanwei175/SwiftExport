using Refit;
using SwiftExport.Contracts.Apis.BaseApi;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.DTOs.Customers.Requests;
using SwiftExport.Contracts.DTOs.Customers.Responses;
using SwiftExport.Contracts.Wrappers;
using System.Threading.Tasks;
using System.Threading;
using System.Collections.Generic;

namespace SwiftExport.Contracts.Apis.CustomerApis
{
    /// <summary>客户管理资料的全量、分页读取及批量保存契约。</summary>
    public interface ICustomerApi : IGenericApi<CustomerDto>
    {
        /// <summary>读取管理范围内全部未删除客户，供客户端本地筛选与编辑，包含并发版本。</summary>
        [Get("/all")]
        Task<ServiceResult<IReadOnlyList<CustomerDto>>> GetAllAsync(CancellationToken cancellationToken = default);

        [Post("/page")]
        Task<ServiceResult<PagedResponseBase<CustomerDto>>> GetPagedAsync([Body] CustomerSearchRequest request);

        [Post("/batch-save")]
        Task<ServiceResult<CustomerBatchSaveResponse>> BatchSaveAsync([Body] CustomerBatchSaveRequest request);

    }
}
