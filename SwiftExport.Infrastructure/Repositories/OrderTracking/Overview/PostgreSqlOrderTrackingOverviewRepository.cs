using System.Text;
using Dapper;
using Npgsql;
using NpgsqlTypes;
using SwiftExport.Contracts.DTOs.OrderTracking.Overview;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.OrderTracking.Overview;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.OrderTracking.Overview;

/// <summary>仅从成品需求及来源显示数据读取跟踪总览，不查询物料、采购和交付。</summary>
public sealed class PostgreSqlOrderTrackingOverviewRepository(RepositoryBaseContext context) : IOrderTrackingOverviewRepository
{
    /// <summary>计数与取页复用筛选，超末页仍返回总数，借用当前会话和可空事务。</summary>
    public async Task<PagedResponseBase<OrderTrackingListItemDto>> SearchAsync(OrderTrackingSearchRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var pageIndex = Math.Max(1, request.PageIndex);
        var pageSize = request.PageSize <= 0 ? 20 : Math.Min(200, request.PageSize);
        var (where, parameters) = OrderTrackingFilters.Build(request);
        var count = await context.Session.Connection.ExecuteScalarAsync<int>(new CommandDefinition(
            $"SELECT count(*)::integer {OrderTrackingFactsSql.From} {where}", new OrderTrackingFilters.Parameters(parameters), context.Session.Transaction, cancellationToken: cancellationToken));
        parameters.Add("Offset", (long)(pageIndex - 1) * pageSize);
        parameters.Add("PageSize", pageSize);
        var rows = await context.Session.Connection.QueryAsync<OrderTrackingListItemDto>(new CommandDefinition($"""
            SELECT {OrderTrackingFactsSql.Columns}
            {OrderTrackingFactsSql.From} {where}
            ORDER BY g.required_date ASC NULLS LAST,g.created_date DESC NULLS LAST,g.uid ASC
            LIMIT @PageSize OFFSET @Offset
            """, new OrderTrackingFilters.Parameters(parameters), context.Session.Transaction, cancellationToken: cancellationToken));
        return new() { Items = rows.AsList(), TotalCount = count, PageIndex = pageIndex, PageSize = pageSize };
    }

    /// <summary>读取已有未删除需求所引用客户的当前代码，候选权限与跟踪查看保持一致。</summary>
    public async Task<List<string>> GetCustomerCodesAsync(CancellationToken cancellationToken = default)
    {
        var rows = await context.Session.Connection.QueryAsync<string>(new CommandDefinition("""
            SELECT min(rtrim(c.customer_code,' ') COLLATE "C") AS "CustomerCode"
            FROM public.customer c
            WHERE c.is_deleted=FALSE AND NULLIF(btrim(c.customer_code,' '),'') IS NOT NULL
                AND EXISTS (SELECT 1 FROM public.gross_demand g WHERE g.customer_id=c.uid AND g.is_deleted=FALSE)
            GROUP BY rtrim(c.customer_code,' ') COLLATE public.erp_dictionary_ci
            ORDER BY rtrim(c.customer_code,' ') COLLATE public.erp_dictionary_ci
            """, transaction: context.Session.Transaction, cancellationToken: cancellationToken));
        return rows.AsList();
    }

}
