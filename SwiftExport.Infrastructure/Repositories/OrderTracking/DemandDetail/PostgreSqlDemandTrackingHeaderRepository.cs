using Dapper;
using SwiftExport.Contracts.DTOs.OrderTracking.DemandDetail;
using SwiftExport.Core.Interfaces.OrderTracking.DemandDetail;
using SwiftExport.Infrastructure.Contexts;

namespace SwiftExport.Infrastructure.Repositories.OrderTracking.DemandDetail;

/// <summary>读取单条需求商业快照和生命周期，不依赖下游完整性。</summary>
public sealed class PostgreSqlDemandTrackingHeaderRepository(RepositoryBaseContext context) : IDemandTrackingHeaderRepository
{
    /// <summary>保留需求自身 NULL 和原始文本，只将有效 SO 头字段作为当前来源补充显示。</summary>
    public Task<DemandTrackingHeaderDto?> GetDemandHeaderAsync(Guid grossDemandId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return context.Session.Connection.QuerySingleOrDefaultAsync<DemandTrackingHeaderDto>(new CommandDefinition($"""
            SELECT {OrderTrackingFactsSql.Columns},
                g.source_version AS "SourceVersion",g.remark AS "Remark",
                g.lifecycle_changed_at AS "LifecycleChangedAt",g.lifecycle_changed_by AS "LifecycleChangedBy",
                g.lifecycle_changed_reason AS "LifecycleChangedReason",g.created_date AS "CreatedDate",
                g.created_by AS "CreatedBy",g.updated_date AS "UpdatedDate",g.updated_by AS "UpdatedBy"
            {OrderTrackingFactsSql.From}
            WHERE {OrderTrackingFactsSql.Eligible} AND g.uid=@GrossDemandId
            """, new { GrossDemandId = grossDemandId }, context.Session.Transaction, cancellationToken: cancellationToken));
    }
}
