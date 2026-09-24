using Dapper;
using SwiftExport.Core.Entities.Procurement;
using SwiftExport.Core.Interfaces.Procurement;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;

namespace SwiftExport.Infrastructure.Repositories.Procurement.PurchaseOrders.Progress;

public sealed class SupplierProgressReportRepository(RepositoryBaseContext context) : ISupplierProgressReportRepository
{
    private DbSession Session => context.Session;
    public Task<SupplierProgressReport?> GetLatestForUpdateAsync(Guid purchaseOrderLineId)
        => Session.Connection.QuerySingleOrDefaultAsync<SupplierProgressReport>($"SELECT TOP(1) {Columns} FROM dbo.SupplierProgressReport f WITH(UPDLOCK,HOLDLOCK) WHERE f.PurchaseOrderLineId=@LineId AND ISNULL(f.IsDeleted,0)=0 ORDER BY f.Sequence DESC,f.Uid DESC;", new { LineId = purchaseOrderLineId }, Session.Transaction);

    public async Task<int> InsertAsync(SupplierProgressReport report)
    {
        context.AuditStamp.StampForInsert(report);
        // SupplierReportedDate 是业务可见的跟进时间，必须与后台审计打戳的创建时间完全一致，不接受客户端时间。
        report.SupplierReportedDate = report.CreatedDate
            ?? throw new InvalidOperationException("供应商进度报告缺少后台创建时间。");
        return await Session.Connection.ExecuteAsync("""
            INSERT dbo.SupplierProgressReport(Uid,Version,IsDeleted,CreatedDate,CreatedBy,UpdatedDate,UpdatedBy,
              PurchaseOrderLineId,Note,Sequence,ArrangementStatus,ReadyQty,ExpectedReadyDate,
              SupplierReportedDate,IsCorrection,CorrectionReason,CommandId)
            VALUES(@Uid,@Version,@IsDeleted,@CreatedDate,@CreatedBy,@UpdatedDate,@UpdatedBy,
              @PurchaseOrderLineId,@Note,@Sequence,@ArrangementStatus,@ReadyQty,@ExpectedReadyDate,
              @SupplierReportedDate,@IsCorrection,@CorrectionReason,@CommandId);
            """, report, Session.Transaction);
    }

    private const string Columns = "f.Uid,f.Version,f.IsDeleted,f.CreatedDate,f.CreatedBy,f.UpdatedDate,f.UpdatedBy,f.PurchaseOrderLineId,f.Sequence,f.ArrangementStatus,f.ReadyQty,f.ExpectedReadyDate,f.SupplierReportedDate,f.Note,f.IsCorrection,f.CorrectionReason,f.CommandId";
}
