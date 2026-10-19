using Dapper;
using SwiftExport.Contracts.DTOs.OrderTracking.Overview;
using SwiftExport.Contracts.DTOs.OrderTracking.Sources;
using SwiftExport.Core.Interfaces.OrderTracking.Overview;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Repositories.Base;
namespace SwiftExport.Infrastructure.Repositories.OrderTracking.Overview;
/// <summary>完整来源子表只按身份读取，不包含任何分页或主表筛选。</summary>
public sealed class PostgreSqlOrderTrackingOverviewRepository(RepositoryBaseContext context) : IOrderTrackingOverviewRepository
{
    /// <summary>保留关闭/作废需求、原始空值及缺失关联，全部结果一次完整缓冲。</summary>
    public async Task<OrderTrackingSourceDemandsDto> GetSourceDemandsAsync(string sourceType,Guid sourceGroupId,CancellationToken cancellationToken=default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var parameters=new PostgreSqlParameters();parameters.Add("SourceType",sourceType);parameters.Add("SourceGroupId",sourceGroupId);
        var rows=await context.Session.Connection.QueryAsync<OrderTrackingListItemDto>(new CommandDefinition($"""
            SELECT {OrderTrackingFactsSql.Columns} {OrderTrackingFactsSql.From}
            WHERE {OrderTrackingFactsSql.Eligible} AND {OrderTrackingFilters.GroupType}=@SourceType AND {OrderTrackingFilters.GroupId}=@SourceGroupId
            ORDER BY g.source_line_number ASC NULLS LAST,g.uid
            """,parameters,context.Session.Transaction,cancellationToken:cancellationToken));
        return new(){SourceType=sourceType,SourceGroupId=sourceGroupId,Items=rows.AsList()};
    }
    /// <summary>读取已有未删除需求客户的当前代码，不要求销售查看权限。</summary>
    public async Task<List<string>> GetCustomerCodesAsync(CancellationToken cancellationToken=default)
        =>(await context.Session.Connection.QueryAsync<string>(new CommandDefinition("""
            SELECT min(rtrim(c.customer_code,' ') COLLATE "C") AS "CustomerCode"
            FROM public.customer c WHERE c.is_deleted=FALSE AND NULLIF(btrim(c.customer_code,' '),'') IS NOT NULL
                AND EXISTS(SELECT 1 FROM public.gross_demand g WHERE g.customer_id=c.uid AND g.is_deleted=FALSE)
            GROUP BY rtrim(c.customer_code,' ') COLLATE public.erp_dictionary_ci
            ORDER BY rtrim(c.customer_code,' ') COLLATE public.erp_dictionary_ci
            """,transaction:context.Session.Transaction,cancellationToken:cancellationToken))).AsList();
}
