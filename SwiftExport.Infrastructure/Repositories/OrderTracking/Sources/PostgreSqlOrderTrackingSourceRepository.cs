using SwiftExport.Contracts.DTOs.OrderTracking.Sources;
using SwiftExport.Contracts.Wrappers;
using Dapper;
using SwiftExport.Infrastructure.Repositories.OrderTracking.Deliveries;
using SwiftExport.Core.Interfaces.OrderTracking.Sources;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Repositories.Base;
namespace SwiftExport.Infrastructure.Repositories.OrderTracking.Sources;
/// <summary>按同一需求行条件筛选来源组，并在稳定来源身份内读取完整命中集合。</summary>
public sealed class PostgreSqlOrderTrackingSourceRepository(RepositoryBaseContext context) : IOrderTrackingSourceRepository
{
    /// <summary>匹配数参与选组，全部条数和客户一致性独立从完整组计算。</summary>
    public async Task<PagedResponseBase<OrderTrackingSourceGroupDto>> SearchSourcesAsync(OrderTrackingSourceSearchRequest request,DateTime businessDate,CancellationToken cancellationToken=default)
    {
        ArgumentNullException.ThrowIfNull(request);cancellationToken.ThrowIfCancellationRequested();
        var (where,parameters)=OrderTrackingFilters.Build(request);
        var (prefix,matchedWhere)=BuildMatchingScope(where,parameters,request.AttentionCodes,businessDate);
        var cte=prefix+$"""
            matched AS (
                SELECT {OrderTrackingFilters.GroupType} AS group_type,{OrderTrackingFilters.GroupId} AS group_id,count(*)::integer AS matched_count
                {OrderTrackingFactsSql.From} {matchedWhere}
                GROUP BY {OrderTrackingFilters.GroupType},{OrderTrackingFilters.GroupId}
            )
            """;
        var count=await context.Session.Connection.ExecuteScalarAsync<int>(new CommandDefinition(cte+" SELECT count(*)::integer FROM matched",
            new OrderTrackingFilters.Parameters(parameters),context.Session.Transaction,cancellationToken:cancellationToken));
        var pageIndex=Math.Max(1,request.PageIndex);var pageSize=request.PageSize<=0?20:Math.Min(200,request.PageSize);
        parameters.Add("Offset",(long)(pageIndex-1)*pageSize);parameters.Add("PageSize",pageSize);
        var rows=await context.Session.Connection.QueryAsync<OrderTrackingSourceGroupDto>(new CommandDefinition(cte+$"""
            SELECT m.group_id AS "SourceGroupId",m.group_type AS "SourceType",facts.source_uid AS "SourceUid",facts.source_no AS "SourceNo",
                CASE WHEN facts.customer_count=1 THEN facts.customer_code END AS "CustomerCode",
                CASE WHEN facts.customer_count=1 THEN facts.customer_name END AS "CustomerName",
                facts.customer_po AS "CustomerPo",facts.invoice_no AS "InvoiceNo",facts.total_count AS "TotalDemandCount",
                m.matched_count AS "MatchedDemandCount",
                concat_ws('；',CASE WHEN facts.source_uid='00000000-0000-0000-0000-000000000000'::uuid THEN '来源身份缺失，按需求身份隔离' END,
                    CASE WHEN facts.source_count>1 OR facts.source_no_count>1 THEN '来源身份或单号不一致' END,
                    CASE WHEN facts.customer_count>1 THEN '来源组客户不一致' WHEN facts.missing_customer THEN '来源客户缺失' END) AS "ReviewReason"
            FROM matched m JOIN LATERAL (
                SELECT min(g.source_uid::text)::uuid AS source_uid,min(g.source_no COLLATE "C") AS source_no,count(*)::integer AS total_count,
                    count(DISTINCT g.source_uid) AS source_count,count(DISTINCT COALESCE(g.source_no,'') COLLATE "C") AS source_no_count,
                    count(DISTINCT COALESCE(g.customer_id,'00000000-0000-0000-0000-000000000000'::uuid)) AS customer_count,
                    bool_or(g.customer_id IS NULL OR g.customer_id='00000000-0000-0000-0000-000000000000'::uuid OR c.uid IS NULL) AS missing_customer,
                    min(c.customer_code COLLATE "C") AS customer_code,min(c.customer_name COLLATE "C") AS customer_name,
                    min(so.customer_po_no COLLATE "C") AS customer_po,min(so.invoice_no COLLATE "C") AS invoice_no
                {OrderTrackingFactsSql.From}
                WHERE {OrderTrackingFactsSql.Eligible} AND {OrderTrackingFilters.GroupId}=m.group_id AND {OrderTrackingFilters.GroupType}=m.group_type
            ) facts ON TRUE
            ORDER BY m.group_type,facts.source_no COLLATE public.erp_dictionary_ci NULLS LAST,m.group_id
            LIMIT @PageSize OFFSET @Offset
            """,new OrderTrackingFilters.Parameters(parameters),context.Session.Transaction,cancellationToken:cancellationToken));
        return new(){Items=rows.AsList(),TotalCount=count,PageIndex=pageIndex,PageSize=pageSize};
    }

    /// <summary>先限定稳定来源，再在同一需求行上求条件交集；一次读取全部身份，不分页或逐行查明细。</summary>
    public async Task<List<Guid>> GetSourceMatchesAsync(string sourceType,Guid sourceGroupId,OrderTrackingSourceMatchRequest request,DateTime businessDate,CancellationToken cancellationToken=default)
    {
        ArgumentNullException.ThrowIfNull(request);cancellationToken.ThrowIfCancellationRequested();
        var (where,parameters)=OrderTrackingFilters.Build(request);
        parameters.Add("SourceType",sourceType);parameters.Add("SourceGroupId",sourceGroupId);
        where+=$" AND {OrderTrackingFilters.GroupType}=@SourceType AND {OrderTrackingFilters.GroupId}=@SourceGroupId";
        var (prefix,matchedWhere)=BuildMatchingScope(where,parameters,request.AttentionCodes,businessDate);
        var rows=await context.Session.Connection.QueryAsync<Guid>(new CommandDefinition(prefix+$"""
            matched AS (SELECT DISTINCT g.uid AS "GrossDemandId" {OrderTrackingFactsSql.From} {matchedWhere})
            SELECT "GrossDemandId" FROM matched ORDER BY "GrossDemandId"
            """,new OrderTrackingFilters.Parameters(parameters),context.Session.Transaction,cancellationToken:cancellationToken));
        return rows.AsList();
    }

    /// <summary>搜索选组和组内命中共用关注项并集；基础条件与关注项始终在同一需求身份上求交。</summary>
    private static (string Prefix,string Where) BuildMatchingScope(string where,PostgreSqlParameters parameters,IReadOnlyCollection<string>? attentionCodes,DateTime businessDate)
    {
        if(attentionCodes is not {Count:>0}) return ("WITH ",where);
        parameters.AddTimestamp("BusinessDate",businessDate.Date);
        var prefix="WITH "+SwiftExport.Infrastructure.Repositories.OrderTracking.Summary.OrderTrackingSummarySql.Attention(
            $"g.uid IN(SELECT g.uid {OrderTrackingFactsSql.From} {where})",attentionCodes)+",";
        return (prefix,where+" AND EXISTS(SELECT 1 FROM summary_attention a WHERE a.gross_id=g.uid)");
    }
}
