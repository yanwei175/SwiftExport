using Dapper;
using SwiftExport.Contracts.DTOs.OrderTracking.MaterialDetail;
using SwiftExport.Contracts.DTOs.OrderTracking.Materials;
using SwiftExport.Core.Interfaces.OrderTracking.MaterialDetail;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Repositories.OrderTracking.Materials;
namespace SwiftExport.Infrastructure.Repositories.OrderTracking.MaterialDetail;
/// <summary>按稳定物料身份保留历史快照，缺失来源不遮挡已有物料。</summary>
public sealed class PostgreSqlMaterialTrackingDetailRepository(RepositoryBaseContext context) : IMaterialTrackingDetailRepository
{
    /// <summary>分别缓冲基础来源与物料快照，不以生命周期或软删除过滤历史行。</summary>
    public async Task<MaterialTrackingDetailDto?> GetMaterialDetailAsync(Guid netDemandId,CancellationToken cancellationToken=default)
    {
        using var reader=await context.Session.Connection.QueryMultipleAsync(new CommandDefinition($"""
            SELECT n.uid AS "NetDemandId",n.gross_demand_id AS "GrossDemandId",g.source_type AS "SourceType",g.source_no AS "SourceNo",
                g.source_line_number AS "SourceLineNumber",c.customer_code AS "CustomerCode",n.parent_sku_snapshot AS "ParentSku",
                n.customer_sku_snapshot AS "CustomerSku",n.color_snapshot AS "Color",n.specification_snapshot AS "Specification",
                n.bom_path AS "BomPath",n.bom_version AS "BomVersion",n.remark AS "Remark",n.rolled_back_at AS "RolledBackAt",
                n.rolled_back_by AS "RolledBackBy",n.rolled_back_reason AS "RolledBackReason"
            FROM public.net_demand n LEFT JOIN public.gross_demand g ON g.uid=n.gross_demand_id
            LEFT JOIN public.customer c ON c.uid=g.customer_id AND c.is_deleted=FALSE WHERE n.uid=@NetDemandId;
            SELECT {OrderTrackingMaterialSql.Columns}
            FROM public.net_demand n LEFT JOIN public.gross_demand g ON g.uid=n.gross_demand_id WHERE n.uid=@NetDemandId;
            """,new{NetDemandId=netDemandId},context.Session.Transaction,cancellationToken:cancellationToken));
        var detail=await reader.ReadSingleOrDefaultAsync<MaterialTrackingDetailDto>();
        var material=await reader.ReadSingleOrDefaultAsync<DemandTrackingMaterialDto>();
        if(detail is not null)detail.Material=material!;
        return detail;
    }
}
