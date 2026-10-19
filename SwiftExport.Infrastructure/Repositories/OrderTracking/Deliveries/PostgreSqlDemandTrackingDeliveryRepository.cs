using Dapper;
using SwiftExport.Contracts.DTOs.OrderTracking.Deliveries;
using SwiftExport.Core.Interfaces.OrderTracking.Deliveries;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.OrderTracking.Deliveries;

/// <summary>交付明细与主表共用批量事实口径，不读取采购或收货区域。</summary>
public sealed class PostgreSqlDemandTrackingDeliveryRepository(RepositoryBaseContext context) : IDemandTrackingDeliveryRepository
{
    /// <summary>同一业务日计算净交付和逾期；正常删除草稿排除，正式异常证据保留。</summary>
    public async Task<DemandTrackingDeliverySectionDto> GetDeliveriesAsync(Guid grossDemandId,DateTime businessDate,CancellationToken cancellationToken=default)
    {
        var parameters=new PostgreSqlParameters();parameters.Add("GrossDemandId",grossDemandId);parameters.AddTimestamp("BusinessDate",businessDate.Date);
        var ctes="WITH "+OrderTrackingDeliverySql.Ctes("g.uid=@GrossDemandId");
        var result=await context.Session.Connection.QuerySingleOrDefaultAsync<DemandTrackingDeliverySectionDto>(new CommandDefinition(ctes+"""
             SELECT gross_id AS "GrossDemandId",unit AS "Unit",business_date AS "BusinessDate",
                delivered_qty AS "DeliveredQty",remaining_qty AS "RemainingQty",is_overdue AS "IsOverdue",
                overdue_days AS "OverdueDays",review_reason AS "ReviewReason" FROM delivery_facts
            """,parameters,context.Session.Transaction,cancellationToken:cancellationToken));
        var items=(await context.Session.Connection.QueryAsync<DemandTrackingDeliveryDto>(new CommandDefinition(ctes+"""
             SELECT head_id AS "PackingListId",line_id AS "PackingListLineId",document_no AS "PackingListNo",line_number AS "LineNumber",
                qty AS "Qty",unit AS "Unit",COALESCE(posting_status=1 AND review_reason='',FALSE) AS "CountsTowardDelivery",
                posted_date AS "PostedDate",reversed_date AS "ReversedDate",review_reason AS "ReviewReason"
             FROM delivery_evidence WHERE posting_status IS DISTINCT FROM 0 OR review_reason<>''
             ORDER BY posted_date NULLS LAST,head_id,line_number,line_id
            """,parameters,context.Session.Transaction,cancellationToken:cancellationToken))).AsList();
        result??=new(){GrossDemandId=grossDemandId,BusinessDate=businessDate.Date,ReviewReason="成品需求已不存在"};
        result.Items=items;
        return result;
    }
}
