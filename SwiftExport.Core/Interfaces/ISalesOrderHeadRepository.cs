using SwiftExport.Contracts.DTOs.SalesOrders.Workspace;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Orders;

namespace SwiftExport.Core.Interfaces;

public interface ISalesOrderHeadRepository : IRepository<SalesOrderHeader>
{
    Task<PagedResponseBase<SalesOrderListItemDto>> SearchWorkspaceAsync(SalesOrderSearchRequest request);
    Task<SalesOrderDocumentDto?> GetWorkspaceDocumentAsync(Guid uid);
    Task<RepositoryWriteResult> SetConfirmationStatusAsync(Guid uid, int version, ConfirmationStatusEnum status);
    Task<RepositoryWriteResult> CloseWorkspaceAsync(Guid uid, int version, string reason);
    Task<RepositoryWriteResult> DeleteDraftAsync(Guid uid, int version);
}
