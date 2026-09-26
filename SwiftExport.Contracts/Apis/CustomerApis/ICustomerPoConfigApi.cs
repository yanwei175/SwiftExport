using Refit;
using SwiftExport.Contracts.Apis.BaseApi;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.DTOs.Customers.Requests;
using SwiftExport.Contracts.DTOs.Customers.Responses;
using SwiftExport.Contracts.Wrappers;
using System;
using System.Threading;
using System.Threading.Tasks;
namespace SwiftExport.Contracts.Apis.CustomerApis
{
    /// <summary>定义客户采购默认配置的客户端 HTTP 契约。</summary>
    public interface ICustomerPoConfigApi : IGenericApi<CustomerPoConfigDto>
    {
        /// <summary>分页查询客户采购默认配置。</summary>
        [Post("/page")]
        Task<ServiceResult<PagedResponseBase<CustomerPoConfigDto>>> GetPagedAsync(
            [Body] CustomerPoConfigSearchRequest request,
            CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>按客户与供应商标识查询完整的只读采购默认配置。</summary>
        [Get("/by-parties")]
        Task<ServiceResult<CustomerPoConfigDto>> GetByPartiesAsync(
            Guid customerId,
            Guid supplierId,
            CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>原子保存客户采购默认配置变更。</summary>
        [Post("/batch-save")]
        Task<ServiceResult<CustomerPoConfigBatchSaveResponse>> BatchSaveAsync([Body] CustomerPoConfigBatchSaveRequest request);
    }
}
