using Dapper;
using SwiftExport.Contracts.DTOs.OrderTracking.Materials;
using SwiftExport.Core.Interfaces.OrderTracking.Materials;
using SwiftExport.Infrastructure.Contexts;
namespace SwiftExport.Infrastructure.Repositories.OrderTracking.Materials;
/// <summary>只读物料采用时快照、根子关系及全部修订，不依赖采购和收货。</summary>
public sealed class PostgreSqlDemandTrackingMaterialRepository(RepositoryBaseContext context) : IDemandTrackingMaterialRepository
{
    /// <summary>历史及软删除修订保留原值，数量不跨修订或单位汇总。</summary>
    public async Task<List<DemandTrackingMaterialDto>> GetMaterialsAsync(Guid grossDemandId,CancellationToken cancellationToken=default)
        =>(await context.Session.Connection.QueryAsync<DemandTrackingMaterialDto>(new CommandDefinition($"""
            SELECT n.uid AS "NetDemandId",n.root_net_demand_id AS "RootNetDemandId",n.parent_net_demand_id AS "ParentNetDemandId",
                n.decomposition_revision AS "DecompositionRevision",n.line_number AS "LineNumber",n.lifecycle_status AS "LifecycleStatus",n.is_deleted AS "IsDeleted",
                n.sku_snapshot AS "Sku",n.description_snapshot AS "Description",n.material_type_snapshot AS "MaterialType",n.supply_type_snapshot AS "SupplyType",
                n.unit_snapshot AS "Unit",n.gross_qty AS "GrossQty",n.net_qty AS "NetQty",n.required_date AS "RequiredDate",
                {OrderTrackingEvidenceSql.MaterialReview} AS "ReviewReason"
            FROM public.net_demand n JOIN public.gross_demand g ON g.uid=n.gross_demand_id AND g.is_deleted=FALSE
            WHERE n.gross_demand_id=@GrossDemandId ORDER BY n.decomposition_revision DESC,n.line_number,n.uid
            """,new{GrossDemandId=grossDemandId},context.Session.Transaction,cancellationToken:cancellationToken))).AsList();
}
