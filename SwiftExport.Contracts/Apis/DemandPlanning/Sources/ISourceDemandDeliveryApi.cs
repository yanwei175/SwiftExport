using Refit;
using SwiftExport.Contracts.DTOs.DemandPlanning.SourceDemandDelivery;
using SwiftExport.Contracts.Wrappers;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace SwiftExport.Contracts.Apis.DemandPlanning
{
    /// <summary>提供来源需求统一可交付查询，不暴露来源接收写入口。</summary>
    public interface ISourceDemandDeliveryApi
    {
        [Post("/search")]
        Task<ServiceResult<PagedResponseBase<SourceDemandDeliveryItemDto>>> SearchAsync(
            [Body] SourceDemandDeliverySearchRequest request,
            CancellationToken cancellationToken = default(CancellationToken));

        [Get("/{grossDemandId}")]
        Task<ServiceResult<SourceDemandDeliveryItemDto>> GetAsync(
            Guid grossDemandId,
            CancellationToken cancellationToken = default(CancellationToken));
    }
}
