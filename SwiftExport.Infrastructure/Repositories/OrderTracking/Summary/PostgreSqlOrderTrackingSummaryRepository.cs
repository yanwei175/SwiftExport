using System.Text.Json;
using Dapper;
using SwiftExport.Contracts.DTOs.OrderTracking.Summary;
using SwiftExport.Core.Interfaces.OrderTracking.Summary;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Repositories.Base;
namespace SwiftExport.Infrastructure.Repositories.OrderTracking.Summary;
/// <summary>按稳定来源身份批量读取摘要，复用参数化证据CTE；事务由调用方持有。</summary>
public sealed class PostgreSqlOrderTrackingSummaryRepository(RepositoryBaseContext context):IOrderTrackingSummaryRepository
{
    private static string Scope => $"""
        EXISTS(SELECT 1 FROM jsonb_to_recordset(CAST(@Sources AS jsonb)) AS k(type text,id uuid)
          WHERE k.type COLLATE public.erp_dictionary_ci={OrderTrackingFilters.GroupType} AND k.id={OrderTrackingFilters.GroupId})
        """;
    /// <summary>内部维度白名单选择固定SQL；数据库失败和取消交给服务按维度处理。</summary>
    public async Task<List<OrderTrackingDemandSummaryDto>> ReadDimensionAsync(IReadOnlyCollection<OrderTrackingSourceKeyDto> sources,string dimension,DateTime businessDate,CancellationToken cancellationToken=default)
    {
        var cte=OrderTrackingSummarySql.Base(Scope,dimension=="Base");
        var select=dimension switch {
            "Base"=>"""
                SELECT gross_id AS "GrossDemandId",source_type AS "SourceType",group_id AS "SourceGroupId",
                  version AS "DemandVersion",unit_snapshot AS "Unit",lifecycle_status=0 AS "IsActive",
                  source_review AS "HasSourceReview",lifecycle_status=0 AND required_date IS NULL AS "MissingRequiredDate"
                FROM summary_base ORDER BY gross_id
                """,
            "Delivery"=>"""
                SELECT d.gross_id AS "GrossDemandId",b.version AS "DemandVersion",b.unit_snapshot AS "Unit",d.delivered_qty AS "DeliveredQty",d.remaining_qty AS "RemainingQty",
                  d.is_overdue AS "IsOverdue",d.overdue_days AS "OverdueDays",
                  CASE WHEN b.lifecycle_status<>0 THEN 'Inactive' WHEN remaining_qty IS NULL OR remaining_qty<0 THEN 'Review'
                    WHEN remaining_qty=0 THEN 'Delivered' WHEN delivered_qty=0 THEN 'NotDelivered' ELSE 'Partial' END AS "DeliveryStatus"
                FROM delivery_facts d JOIN summary_base b ON b.gross_id=d.gross_id
                """,
            "Material"=>"""
                SELECT m.gross_id AS "GrossDemandId",b.version AS "DemandVersion",b.unit_snapshot AS "Unit",active_count AS "ActiveMaterialCount",history_count AS "HistoricalMaterialCount",
                  review_count AS "MaterialReviewCount" FROM summary_material_counts m JOIN summary_base b ON b.gross_id=m.gross_id
                """,
            "Purchase"=>"""
                SELECT p.gross_id AS "GrossDemandId",b.version AS "DemandVersion",b.unit_snapshot AS "Unit",applicable_count AS "ApplicableMaterialCount",
                  shortfall_count AS "PurchaseShortfallMaterialCount",excess_count AS "PurchaseExcessMaterialCount",
                  review_count AS "PurchaseReviewMaterialCount",
                  CASE WHEN b.lifecycle_status<>0 THEN 'Inactive' WHEN review_count>0 THEN 'Review'
                    WHEN applicable_count=0 THEN 'NotApplicable' WHEN excess_count>0 THEN 'Excess'
                    WHEN shortfall_count=0 THEN 'Allocated' WHEN allocated_count=0 THEN 'Unallocated' ELSE 'Partial' END AS "PurchaseStatus"
                FROM summary_purchase_counts p JOIN summary_base b ON b.gross_id=p.gross_id
                """,
            "Receipt"=>"""
                SELECT r.gross_id AS "GrossDemandId",b.version AS "DemandVersion",b.unit_snapshot AS "Unit",line_count AS "ReceiptPurchaseLineCount",completed_count AS "ReceiptCompletedLineCount",
                  outstanding_count AS "ReceiptOutstandingLineCount",review_count AS "ReceiptReviewLineCount",excess_count AS "ReceiptExcessLineCount",
                  shared_count AS "SharedPurchaseLineCount",
                  CASE WHEN b.lifecycle_status<>0 THEN 'Inactive' WHEN review_count>0 THEN 'Review'
                    WHEN line_count=0 THEN 'None' WHEN excess_count>0 THEN 'Excess'
                    WHEN outstanding_count>0 THEN 'Outstanding' ELSE 'Completed' END AS "ReceiptStatus"
                FROM summary_receipt_counts r JOIN summary_base b ON b.gross_id=r.gross_id
                """,
            _=>throw new ArgumentOutOfRangeException(nameof(dimension))
        };
        cte+=dimension switch {
            "Delivery"=>","+OrderTrackingSummarySql.Delivery,
            "Material"=>","+OrderTrackingSummarySql.Material,
            "Purchase"=>","+OrderTrackingSummarySql.Material+","+OrderTrackingSummarySql.Purchase,
            "Receipt"=>","+OrderTrackingSummarySql.Receipts,
            _=>""};
        return (await context.Session.Connection.QueryAsync<OrderTrackingDemandSummaryDto>(new CommandDefinition("WITH "+cte+" "+select,
            Parameters(sources,businessDate),context.Session.Transaction,cancellationToken:cancellationToken))).AsList();
    }
    /// <summary>来源内采购行去重后统计；不同来源允许各自展示同一共享行。</summary>
    public async Task<List<OrderTrackingSourceSummaryDto>> ReadSourceReceiptsAsync(IReadOnlyCollection<OrderTrackingSourceKeyDto> sources,DateTime businessDate,CancellationToken cancellationToken=default)
    {
        var sql="WITH "+OrderTrackingSummarySql.Base(Scope)+","+OrderTrackingSummarySql.Receipts+"""
            , source_lines AS (
              SELECT DISTINCT b.source_type,b.group_id,r.line_id,r.remaining_qty,r.shared
              FROM summary_base b JOIN summary_receipt_scope r ON r.gross_id=b.gross_id WHERE b.lifecycle_status=0
            ), groups AS (SELECT DISTINCT source_type,group_id FROM summary_base)
            SELECT g.source_type AS "SourceType",g.group_id AS "SourceGroupId",count(l.line_id)::int AS "ReceiptPurchaseLineCount",
              count(l.line_id) FILTER(WHERE remaining_qty=0)::int AS "ReceiptCompletedLineCount",
              count(l.line_id) FILTER(WHERE remaining_qty>0)::int AS "ReceiptOutstandingLineCount",
              count(l.line_id) FILTER(WHERE remaining_qty IS NULL)::int AS "ReceiptReviewLineCount",
              count(l.line_id) FILTER(WHERE remaining_qty<0)::int AS "ReceiptExcessLineCount",
              count(l.line_id) FILTER(WHERE shared)::int AS "SharedPurchaseLineCount"
            FROM groups g LEFT JOIN source_lines l ON l.source_type=g.source_type AND l.group_id=g.group_id
            GROUP BY g.source_type,g.group_id
            """;
        return (await context.Session.Connection.QueryAsync<OrderTrackingSourceSummaryDto>(new CommandDefinition(sql,Parameters(sources,businessDate),context.Session.Transaction,cancellationToken:cancellationToken))).AsList();
    }
    /// <summary>只序列化身份值；SQL始终参数化，不把来源类型拼接进查询。</summary>
    private static PostgreSqlParameters Parameters(IReadOnlyCollection<OrderTrackingSourceKeyDto> sources,DateTime date)
    {
        var result=new PostgreSqlParameters();
        result.Add("Sources",JsonSerializer.Serialize(sources.Select(s=>new{type=s.SourceType,id=s.SourceGroupId})));
        result.AddTimestamp("BusinessDate",date.Date);
        return result;
    }
}
