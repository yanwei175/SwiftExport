using Dapper;
using SwiftExport.Contracts.DTOs.SalesOrders.Fulfillment;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.Orders;
using SwiftExport.Infrastructure.Contexts;
using System.Text;

namespace SwiftExport.Infrastructure.Repositories.Orders;

/// <summary>以销售订单为分页粒度生成只读履约 Manager 投影。</summary>
public sealed class SalesFulfillmentManagerRepository(RepositoryBaseContext context) : ISalesFulfillmentManagerRepository
{
    /// <summary>复用 SO-003 现行正式出库链聚合已发量，缺少来源需求时保留未知语义。</summary>
    public async Task<PagedResponseBase<SalesFulfillmentManagerListItemDto>> SearchAsync(
        SalesFulfillmentManagerSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        var pageIndex = Math.Max(1, request.PageIndex);
        var pageSize = Math.Clamp(request.PageSize, 1, 200);
        var where = new StringBuilder("WHERE h.IsDeleted=0");
        var parameters = new DynamicParameters(new
        {
            SourceType = DocumentTypeStaticsEnum.SalesOrder,
            Offset = (pageIndex - 1) * pageSize,
            PageSize = pageSize
        });
        AddLike(where, parameters, "h.OrderNo", "OrderNo", request.OrderNo);
        AddLike(where, parameters, "h.CustomerCode", "CustomerCode", request.CustomerCode);
        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            parameters.Add("Keyword", $"%{request.Keyword.Trim().Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]")}%");
            where.Append(" AND (h.OrderNo LIKE @Keyword OR h.CustomerCode LIKE @Keyword OR c.CustomerName LIKE @Keyword)");
        }
        if (request.FulfillmentStatuses.Count > 0)
        {
            parameters.Add("Statuses", request.FulfillmentStatuses.Select(value => (int)value).Distinct().ToArray());
            where.Append(" AND totals.FulfillmentStatus IN @Statuses");
        }

        var fromSql = $@"
FROM dbo.SalesOrderHeader h WITH(NOLOCK)
LEFT JOIN dbo.Customer c WITH(NOLOCK) ON c.Uid=h.CustomerId AND c.IsDeleted=0
OUTER APPLY (
 SELECT COUNT(1) LineCount,SUM(l.Qty) DemandQty,SUM(df.DemandCount) SourceDemandCount,
        SUM(CASE WHEN df.DemandCount=0 OR df.ActiveCount>1 THEN 1 ELSE 0 END) AnomalyCount,
        CASE WHEN SUM(CASE WHEN df.DemandCount=0 OR df.ActiveCount>1 THEN 1 ELSE 0 END)>0 THEN NULL ELSE SUM(sf.ShippedQty) END ShippedQty,
        CASE WHEN SUM(CASE WHEN df.DemandCount=0 OR df.ActiveCount>1 THEN 1 ELSE 0 END)>0 THEN NULL ELSE SUM(CASE WHEN l.Qty>sf.ShippedQty THEN l.Qty-sf.ShippedQty ELSE 0 END) END UnshippedQty,
        CASE WHEN SUM(CASE WHEN df.DemandCount=0 OR df.ActiveCount>1 THEN 1 ELSE 0 END)>0 THEN NULL
             WHEN SUM(sf.ShippedQty)<=0 THEN 0 WHEN SUM(sf.ShippedQty)>=SUM(l.Qty) THEN 2 ELSE 1 END FulfillmentStatus
 FROM dbo.SalesOrderLine l WITH(NOLOCK)
 OUTER APPLY (SELECT COUNT(1) DemandCount,COUNT(CASE WHEN d.LifecycleStatus=0 THEN 1 END) ActiveCount FROM dbo.GrossDemand d WITH(NOLOCK)
              WHERE d.SourceType=@SourceType AND d.SourceUid=h.Uid AND d.SourceLineId=l.Uid AND d.IsDeleted=0) df
 OUTER APPLY (SELECT ISNULL(SUM(ol.Quantity),0) ShippedQty
              FROM dbo.OutboundDocumentLine ol WITH(NOLOCK)
              JOIN dbo.OutboundDocument oh WITH(NOLOCK) ON oh.Uid=ol.OutboundDocumentId
              JOIN dbo.NetDemandAllocation a WITH(NOLOCK) ON a.Uid=ol.NetDemandAllocationId
              JOIN dbo.NetDemand nd WITH(NOLOCK) ON nd.Uid=a.NetDemandId
              JOIN dbo.GrossDemand d WITH(NOLOCK) ON d.Uid=nd.GrossDemandId
              WHERE d.SourceType=@SourceType AND d.SourceUid=h.Uid AND d.SourceLineId=l.Uid
                AND oh.DocumentType=20 AND oh.PostingStatus=1 AND oh.IsDeleted=0 AND ol.IsDeleted=0) sf
 WHERE l.SalesOrderId=h.Uid AND l.IsDeleted=0
) totals
{where}";

        var sql = $@"
SELECT COUNT(1) {fromSql};

SELECT h.Uid SalesOrderId,h.Version,h.OrderNo,h.CustomerCode,ISNULL(c.CustomerName,N'') CustomerName,
       h.OrderDate,h.DeliveryDate,ISNULL(totals.LineCount,0) LineCount,ISNULL(totals.SourceDemandCount,0) SourceDemandCount,
       ISNULL(totals.AnomalyCount,0) AnomalyCount,ISNULL(totals.DemandQty,0) DemandQty,
       totals.ShippedQty,totals.UnshippedQty,totals.FulfillmentStatus
{fromSql}
ORDER BY h.OrderDate DESC,h.OrderNo DESC,h.Uid
OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;";
        using var multi = await context.Session.Connection.QueryMultipleAsync(new CommandDefinition(
            sql, parameters, context.Session.Transaction, cancellationToken: cancellationToken));
        var count = await multi.ReadFirstAsync<int>();
        return new PagedResponseBase<SalesFulfillmentManagerListItemDto>
        {
            Items = (await multi.ReadAsync<SalesFulfillmentManagerListItemDto>()).ToList(),
            TotalCount = count, PageIndex = pageIndex, PageSize = pageSize
        };
    }

    private static void AddLike(StringBuilder where, DynamicParameters parameters, string column, string name, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        parameters.Add(name, $"%{value.Trim().Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]")}%");
        where.Append($" AND {column} LIKE @{name}");
    }
}
