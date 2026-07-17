using Dapper;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrders;
using SwiftExport.Core.Entities.Procurement;
using SwiftExport.Core.Interfaces.Procurement;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;

namespace SwiftExport.Infrastructure.Repositories.Procurement;

public sealed class SupplierProgressReportRepository(RepositoryBaseContext context) : ISupplierProgressReportRepository
{
    private DbSession Session => context.Session;
    public Task<SupplierProgressReport?> GetByCommandAsync(Guid commandId)
        => Session.Connection.QuerySingleOrDefaultAsync<SupplierProgressReport>($"SELECT {Columns} FROM dbo.FollowUpLog f WHERE f.CommandId=@CommandId AND f.RefLineType=N'PurchaseOrderLine' AND ISNULL(f.IsDeleted,0)=0;", new { CommandId = commandId }, Session.Transaction);

    public Task<SupplierProgressReport?> GetLatestForUpdateAsync(Guid purchaseOrderLineId)
        => Session.Connection.QuerySingleOrDefaultAsync<SupplierProgressReport>($"SELECT TOP(1) {Columns} FROM dbo.FollowUpLog f WITH(UPDLOCK,HOLDLOCK) WHERE f.RefLineId=@LineId AND f.RefLineType=N'PurchaseOrderLine' AND ISNULL(f.IsDeleted,0)=0 ORDER BY f.Sequence DESC,f.Uid DESC;", new { LineId = purchaseOrderLineId }, Session.Transaction);

    public async Task<IReadOnlyList<SupplierProgressReportDto>> GetByPurchaseOrderAsync(Guid purchaseOrderId, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT f.Uid,f.RefLineId AS PurchaseOrderLineId,f.Sequence,f.ArrangementStatus,f.ReadyQty,
                   f.ExpectedReadyDate,f.SupplierReportedDate,f.NoteContent AS Note,f.IsCorrection,
                   f.CorrectionReason,f.CommandId,f.CreatedDate,f.CreatedBy
            FROM dbo.FollowUpLog f
            INNER JOIN dbo.PurchaseOrderLine l ON l.Uid=f.RefLineId AND l.PurchaseOrderId=@PurchaseOrderId AND ISNULL(l.IsDeleted,0)=0
            WHERE f.RefLineType=N'PurchaseOrderLine' AND ISNULL(f.IsDeleted,0)=0
            ORDER BY l.LineNumber,f.Sequence DESC,f.Uid DESC;
            """;
        var rows = await Session.Connection.QueryAsync<SupplierProgressReportDto>(new CommandDefinition(sql, new { PurchaseOrderId = purchaseOrderId }, Session.Transaction, cancellationToken: cancellationToken));
        return [.. rows];
    }

    public async Task<int> InsertAsync(SupplierProgressReport report)
    {
        context.AuditStamp.StampForInsert(report);
        return await Session.Connection.ExecuteAsync("""
            INSERT dbo.FollowUpLog(Uid,Version,IsDeleted,CreatedDate,CreatedBy,UpdatedDate,UpdatedBy,
              RefLineId,NoteContent,RefLineType,Sequence,ArrangementStatus,ReadyQty,ExpectedReadyDate,
              SupplierReportedDate,IsCorrection,CorrectionReason,CommandId)
            VALUES(@Uid,@Version,@IsDeleted,@CreatedDate,@CreatedBy,@UpdatedDate,@UpdatedBy,
              @PurchaseOrderLineId,@Note,@RefLineType,@Sequence,@ArrangementStatus,@ReadyQty,@ExpectedReadyDate,
              @SupplierReportedDate,@IsCorrection,@CorrectionReason,@CommandId);
            """, report, Session.Transaction);
    }

    private const string Columns = "f.Uid,f.Version,f.IsDeleted,f.CreatedDate,f.CreatedBy,f.UpdatedDate,f.UpdatedBy,f.RefLineType,f.RefLineId AS PurchaseOrderLineId,f.Sequence,f.ArrangementStatus,f.ReadyQty,f.ExpectedReadyDate,f.SupplierReportedDate,f.NoteContent AS Note,f.IsCorrection,f.CorrectionReason,f.CommandId";
}
