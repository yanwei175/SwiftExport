using SwiftExport.Contracts.DTOs.SalesOrders.Detail;
using SwiftExport.Contracts.DTOs.SalesOrders.Manager;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Orders;

namespace SwiftExport.Core.Interfaces;

/// <summary>
/// 提供销售订单头的 Manager 查询、Detail 读取及生命周期持久化能力。
/// </summary>
public interface ISalesOrderHeadRepository : IRepository<SalesOrderHeader>
{
    /// <summary>按管理页条件分页查询销售订单摘要。</summary>
    Task<PagedResponseBase<SalesOrderManagerListItemDto>> SearchManagerAsync(
        SalesOrderManagerSearchRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>读取销售订单完整详情。</summary>
    Task<SalesOrderDetailDto?> GetDetailAsync(
        Guid uid,
        CancellationToken cancellationToken = default);

    /// <summary>按版本更新销售订单确认状态。</summary>
    Task<RepositoryWriteResult> SetConfirmationStatusAsync(Guid uid, int version, ConfirmationStatusEnum status);

    /// <summary>按版本关闭销售订单并记录原因。</summary>
    Task<RepositoryWriteResult> CloseAsync(Guid uid, int version, string reason);

    /// <summary>删除符合条件的销售订单草稿。</summary>
    Task<RepositoryWriteResult> DeleteDraftAsync(Guid uid, int version);
}
