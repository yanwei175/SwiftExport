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
    private static readonly EntityMetadata ReportMap = PostgreSqlEntityMetadata.GetMetadata(typeof(SupplierProgressReport));
    private static readonly string[] Columns = ["Uid","Version","IsDeleted","CreatedDate","CreatedBy","UpdatedDate","UpdatedBy","PurchaseOrderLineId","Sequence","ArrangementStatus","ReadyQty","ExpectedReadyDate","SupplierReportedDate","Note","IsCorrection","CorrectionReason","CommandId"];
    private static readonly string[] InsertColumns = ["Uid","Version","IsDeleted","CreatedDate","CreatedBy","UpdatedDate","UpdatedBy","PurchaseOrderLineId","Note","Sequence","ArrangementStatus","ReadyQty","ExpectedReadyDate","SupplierReportedDate","IsCorrection","CorrectionReason","CommandId"];

    /// <summary>按原序号及SqlGuid倒序锁实际最新一条未删除反馈；IsDeleted NULL仍按false，缺失返回null且不宣称锁住空范围。</summary>
    public Task<SupplierProgressReport?> GetLatestForUpdateAsync(Guid purchaseOrderLineId)
        => context.Session.Connection.QuerySingleOrDefaultAsync<SupplierProgressReport>($"""
            SELECT {string.Join(',', Columns.Select(c => $"f.{Column(c)} AS {PostgreSqlNames.Quote(c)}"))}
            FROM {Table()} f WHERE f.{Column("PurchaseOrderLineId")}=@LineId AND COALESCE(f.{Column("IsDeleted")},FALSE)=FALSE
            ORDER BY f.{Column("Sequence")} DESC NULLS LAST,{SqlGuidOrder("f." + Column("Uid"))} DESC NULLS LAST LIMIT 1 FOR UPDATE;
            """, new { LineId = purchaseOrderLineId }, context.Session.Transaction);

    /// <summary>沿用原插入打戳，令SupplierReportedDate等于CreatedDate；保留调用方Uid/Version，不强加事务守卫，返回实际影响行数。</summary>
    public async Task<int> InsertAsync(SupplierProgressReport report)
    {
        context.AuditStamp.StampForInsert(report);
        // 与原后台创建时间完全一致；已有CreatedDate仍按共享打戳规则保留，不另取当前时间或接受独立报告时间。
        report.SupplierReportedDate = report.CreatedDate
            ?? throw new InvalidOperationException("供应商进度报告缺少后台创建时间。");
        return await context.Session.Connection.ExecuteAsync(
            $"INSERT INTO {Table()} ({string.Join(',', InsertColumns.Select(Column))}) VALUES ({string.Join(',', InsertColumns.Select(c => "@" + c))});",
            PostgreSqlParameters.Entity(report, ReportMap), context.Session.Transaction);
    }

    /// <summary>只映射本仓储固定报告属性，避免泛型新增覆盖原身份或版本。</summary>
    private static string Column(string property) => PostgreSqlNames.Quote(ReportMap.GetProperty(property).ColumnName);

    /// <summary>使用共享实体物理表映射，不新增公共名称规则。</summary>
    private static string Table() => PostgreSqlNames.Quote(ReportMap.SchemaName) + "." + PostgreSqlNames.Quote(ReportMap.TableName);

    /// <summary>同序号报告按原SQL Server uniqueidentifier倒序选择，不能改用PG默认UUID优先级。</summary>
    private static string SqlGuidOrder(string column) => "ARRAY[" + string.Join(',',
        new[] { 10,11,12,13,14,15,8,9,7,6,5,4,3,2,1,0 }.Select(i => $"get_byte(uuid_send({column}),{i})")) + "]";
}
