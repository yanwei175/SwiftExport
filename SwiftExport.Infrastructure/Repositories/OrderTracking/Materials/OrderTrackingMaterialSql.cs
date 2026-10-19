namespace SwiftExport.Infrastructure.Repositories.OrderTracking.Materials;
/// <summary>列表和单物料详情采用同一快照投影及修订核实口径。</summary>
internal static class OrderTrackingMaterialSql
{
    internal static string Columns=>$"""
        n.uid AS "NetDemandId",n.root_net_demand_id AS "RootNetDemandId",n.parent_net_demand_id AS "ParentNetDemandId",
        n.decomposition_revision AS "DecompositionRevision",n.line_number AS "LineNumber",n.lifecycle_status AS "LifecycleStatus",n.is_deleted AS "IsDeleted",
        n.sku_snapshot AS "Sku",n.description_snapshot AS "Description",n.material_type_snapshot AS "MaterialType",n.supply_type_snapshot AS "SupplyType",
        n.unit_snapshot AS "Unit",n.gross_qty AS "GrossQty",n.net_qty AS "NetQty",n.required_date AS "RequiredDate",
        {OrderTrackingEvidenceSql.MaterialReview} AS "ReviewReason"
        """;
}
