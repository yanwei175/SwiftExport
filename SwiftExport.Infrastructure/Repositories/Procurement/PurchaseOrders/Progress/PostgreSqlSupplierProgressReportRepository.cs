using Dapper;
using SwiftExport.Core.Entities.Procurement;
using SwiftExport.Core.Interfaces.Procurement;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.Procurement.PurchaseOrders.Progress;

/// <summary>借用当前PostgreSQL会话锁定最新供应商反馈并追加报告；保留原固定字段和后台创建时间，不接管事务或回执。</summary>
public sealed class PostgreSqlSupplierProgressReportRepository(RepositoryBaseContext context) : ISupplierProgressReportRepository
{
    private const string ReportColumns = """
        f."uid" AS "Uid",
        f."version" AS "Version",
        f."is_deleted" AS "IsDeleted",
        f."created_date" AS "CreatedDate",
        f."created_by" AS "CreatedBy",
        f."updated_date" AS "UpdatedDate",
        f."updated_by" AS "UpdatedBy",
        f."purchase_order_line_id" AS "PurchaseOrderLineId",
        f."sequence" AS "Sequence",
        f."arrangement_status" AS "ArrangementStatus",
        f."ready_qty" AS "ReadyQty",
        f."expected_ready_date" AS "ExpectedReadyDate",
        f."supplier_reported_date" AS "SupplierReportedDate",
        f."note" AS "Note",
        f."is_correction" AS "IsCorrection",
        f."correction_reason" AS "CorrectionReason",
        f."command_id" AS "CommandId"
    """;
    private const string InsertReportSql = """
        INSERT INTO "public"."supplier_progress_report" ("uid",
        "version",
        "is_deleted",
        "created_date",
        "created_by",
        "updated_date",
        "updated_by",
        "purchase_order_line_id",
        "note",
        "sequence",
        "arrangement_status",
        "ready_qty",
        "expected_ready_date",
        "supplier_reported_date",
        "is_correction",
        "correction_reason",
        "command_id") VALUES (@Uid,
        @Version,
        @IsDeleted,
        @CreatedDate,
        @CreatedBy,
        @UpdatedDate,
        @UpdatedBy,
        @PurchaseOrderLineId,
        @Note,
        @Sequence,
        @ArrangementStatus,
        @ReadyQty,
        @ExpectedReadyDate,
        @SupplierReportedDate,
        @IsCorrection,
        @CorrectionReason,
        @CommandId);
    """;

    private static readonly EntityMetadata ReportMap = EntityMetadataCache.GetMetadata(typeof(SupplierProgressReport));

    /// <summary>按原序号及SqlGuid倒序锁实际最新一条未删除反馈；IsDeleted NULL仍按false，缺失返回null且不宣称锁住空范围。</summary>
    public Task<SupplierProgressReport?> GetLatestForUpdateAsync(Guid purchaseOrderLineId)
        => context.Session.Connection.QuerySingleOrDefaultAsync<SupplierProgressReport>($"""
            SELECT {ReportColumns}
            FROM "public"."supplier_progress_report" f WHERE f."purchase_order_line_id"=@LineId AND COALESCE(f."is_deleted",FALSE)=FALSE
            ORDER BY f."sequence" DESC NULLS LAST,{SqlGuidOrder("f.\"uid\"")} DESC NULLS LAST LIMIT 1 FOR UPDATE;
            """, new { LineId = purchaseOrderLineId }, context.Session.Transaction);

    /// <summary>沿用原插入打戳，令SupplierReportedDate等于CreatedDate；保留调用方Uid/Version，不强加事务守卫，返回实际影响行数。</summary>
    public async Task<int> InsertAsync(SupplierProgressReport report)
    {
        context.AuditStamp.StampForInsert(report);
        // 与原后台创建时间完全一致；已有CreatedDate仍按共享打戳规则保留，不另取当前时间或接受独立报告时间。
        report.SupplierReportedDate = report.CreatedDate
            ?? throw new InvalidOperationException("供应商进度报告缺少后台创建时间。");
        return await context.Session.Connection.ExecuteAsync(
            InsertReportSql,
            PostgreSqlParameters.Entity(report, ReportMap), context.Session.Transaction);
    }

    /// <summary>同序号报告按原SQL Server uniqueidentifier倒序选择，不能改用PG默认UUID优先级。</summary>
    private static string SqlGuidOrder(string column) => "ARRAY[" + string.Join(',',
        new[] { 10, 11, 12, 13, 14, 15, 8, 9, 7, 6, 5, 4, 3, 2, 1, 0 }.Select(i => $"get_byte(uuid_send({column}),{i})")) + "]";
}
