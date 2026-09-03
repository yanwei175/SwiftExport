using Dapper;
using SwiftExport.Core.Entities.Procurement;
using SwiftExport.Core.Interfaces.Procurement;
using SwiftExport.Infrastructure.Contexts;
namespace SwiftExport.Infrastructure.Repositories.Procurement;
/// <summary>仅读取确认快照表的历史事实，客户端查看不受当前主数据停用或 BOM 修改影响。</summary>
public sealed class ProcurementConfirmationRepository(RepositoryBaseContext context) : IProcurementConfirmationRepository
{
    /// <inheritdoc/>
    public async Task<List<PurchaseConfirmationLine>> GetLinesAsync(Guid orderId, Guid confirmationId, CancellationToken ct)
        => (await context.Session.Connection.QueryAsync<PurchaseConfirmationLine>(new CommandDefinition("""
            SELECT s.* FROM dbo.PurchaseConfirmationLine s
            JOIN dbo.PurchaseOrderHead h ON h.Uid=s.PurchaseOrderId AND h.IsDeleted=0
            WHERE s.PurchaseOrderId=@orderId AND s.ConfirmationId=@confirmationId ORDER BY s.PurchaseOrderLineId;
            """, new { orderId, confirmationId }, context.Session.Transaction, cancellationToken: ct))).ToList();
    /// <inheritdoc/>
    public async Task<List<PurchaseConfirmationNode>> GetNodesAsync(Guid orderId, Guid confirmationId, CancellationToken ct)
        => (await context.Session.Connection.QueryAsync<PurchaseConfirmationNode>(new CommandDefinition("""
            SELECT n.* FROM dbo.PurchaseConfirmationNode n
            JOIN dbo.PurchaseConfirmationLine s ON s.Uid=n.ConfirmationLineId
            JOIN dbo.PurchaseOrderHead h ON h.Uid=s.PurchaseOrderId AND h.IsDeleted=0
            WHERE s.PurchaseOrderId=@orderId AND s.ConfirmationId=@confirmationId ORDER BY n.ConfirmationLineId,n.Path;
            """, new { orderId, confirmationId }, context.Session.Transaction, cancellationToken: ct))).ToList();
}
