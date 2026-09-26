using SwiftExport.Contracts.DTOs.SalesOrders.Detail;
using SwiftExport.Contracts.DTOs.SalesOrders.Manager;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.SalesOrders;

namespace SwiftExport.Core.Interfaces.SalesOrders.Document;

/// <summary>
/// 在请求级事务中持久化销售订单头和生命周期状态。
/// </summary>
public interface ISalesOrderHeadRepository : IRepository<SalesOrderHeader>
{
    /// <summary>按版本更新销售订单确认状态。</summary>
    Task<RepositoryWriteResult> SetConfirmationStatusAsync(Guid uid, int version, ConfirmationStatusEnum status);

    /// <summary>按版本关闭销售订单并记录原因。</summary>
    Task<RepositoryWriteResult> CloseAsync(Guid uid, int version, string reason);

    /// <summary>删除符合条件的销售订单草稿。</summary>
    Task<RepositoryWriteResult> DeleteDraftAsync(Guid uid, int version);
}
