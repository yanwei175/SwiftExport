using Dapper;
using Npgsql;
using NpgsqlTypes;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderProgress;
using SwiftExport.Core.Interfaces.Procurement;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.Procurement.PurchaseOrders.Progress;

/// <summary>供应商反馈录入详情，只提供选行、最新反馈和完整反馈历史所需上下文。</summary>
public sealed partial class PostgreSqlPurchaseOrderProgressQueryRepository : IPurchaseOrderProgressQueryRepository
{

    /// <summary>按采购单身份读取当前头版本和登记资格，再读取当前行及完整反馈；缺头返回null并释放读取器。</summary>
    public async Task<PurchaseOrderProgressDetailDto?> GetDetailAsync(Guid purchaseOrderId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var parameters = new PostgreSqlParameters();
        parameters.Add("PurchaseOrderId", purchaseOrderId);
        using var grid = await context.Session.Connection.QueryMultipleAsync(new CommandDefinition(
            DetailSql(), new DetailParameters(parameters), context.Session.Transaction, cancellationToken: cancellationToken));
        var detail = await grid.ReadSingleOrDefaultAsync<PurchaseOrderProgressDetailDto>();
        if (detail is null) return null;
        var lines = (await grid.ReadAsync<PurchaseOrderProgressLineDto>()).ToList();
        var reports = (await grid.ReadAsync<SupplierProgressReportDto>()).ToList();
        foreach (var line in lines)
        {
            // 同序号边界与写入端锁定最新报告使用相同SqlGuid优先级，整条快照不拼接旧值。
            line.ProgressHistory = reports.Where(report => report.PurchaseOrderLineId == line.PurchaseOrderLineId)
                .OrderByDescending(report => report.Sequence).ThenByDescending(report => new System.Data.SqlTypes.SqlGuid(report.Uid)).ToList();
            line.LatestProgress = line.ProgressHistory.FirstOrDefault();
        }
        detail.Lines = lines;
        return detail;
    }

    /// <summary>三个语句独立读取当阶段当前事实；报告只复核有效行，不冻结第一阶段行身份或新增头有效性复验。</summary>
    private static string DetailSql() => $"""
        SELECT h."uid" AS "PurchaseOrderId",h."version" AS "Version",h."order_no" AS "OrderNo",h."supplier_name" AS "SupplierName",
            h."confirmation_status" AS "ConfirmationStatus",h."lifecycle_status" AS "LifecycleStatus",{EligibilityColumns}
        FROM "public"."purchase_order_head" h WHERE h."uid"=@PurchaseOrderId AND COALESCE(h."is_deleted",FALSE)=FALSE;

        SELECT l."uid" AS "PurchaseOrderLineId",l."version" AS "Version",l."line_number" AS "LineNumber",
            COALESCE(product."main_image",'') AS "MainImage",
            l."sku" AS "Sku",l."supplier_sku" AS "SupplierSku",l."description_zh" AS "DescriptionZh",
            l."quantity" AS "Quantity",
            l."planned_delivery_date" AS "PlannedDeliveryDate"
        FROM "public"."purchase_order_line" l
        INNER JOIN "public"."purchase_order_head" h ON h."uid"=l."purchase_order_id" AND COALESCE(h."is_deleted",FALSE)=FALSE
        LEFT JOIN "public"."product" product ON product."uid"=l."product_id"
            AND COALESCE(product."is_deleted",FALSE)=FALSE
        WHERE l."purchase_order_id"=@PurchaseOrderId AND COALESCE(l."is_deleted",FALSE)=FALSE
        ORDER BY l."line_number" ASC NULLS FIRST,{UuidOrder("l.\"uid\"")} ASC;

        SELECT {R("Uid")} AS "Uid",{R("PurchaseOrderLineId")} AS "PurchaseOrderLineId",{R("Sequence")} AS "Sequence",
            {R("ArrangementStatus")} AS "ArrangementStatus",{R("ReadyQty")} AS "ReadyQty",
            {R("ExpectedReadyDate")} AS "ExpectedReadyDate",{R("SupplierReportedDate")} AS "SupplierReportedDate",
            {R("Note")} AS "Note",{R("IsCorrection")} AS "IsCorrection",{R("CorrectionReason")} AS "CorrectionReason",
            {R("CommandId")} AS "CommandId",{R("CreatedDate")} AS "CreatedDate",{R("CreatedBy")} AS "CreatedBy"
        FROM "public"."supplier_progress_report" r
        INNER JOIN "public"."purchase_order_line" l ON l."uid"={R("PurchaseOrderLineId")}
            AND l."purchase_order_id"=@PurchaseOrderId AND COALESCE(l."is_deleted",FALSE)=FALSE
        WHERE COALESCE({R("IsDeleted")},FALSE)=FALSE
        ORDER BY l."line_number" ASC NULLS FIRST,{R("Sequence")} DESC NULLS LAST,{UuidOrder(R("Uid"))} DESC;
        """;

    /// <summary>本详情仅绑定采购单身份；沿共享参数克隆，不修改公共参数或驱动推断。</summary>
    private sealed class DetailParameters(PostgreSqlParameters parameters) : SqlMapper.IDynamicParameters
    {
        /// <summary>参数独立归命令所有，数组不展开，查询事务与连接仍由调用者拥有。</summary>
        public void AddParameters(System.Data.IDbCommand command, SqlMapper.Identity identity)
        {
            parameters.AddParameters(command, identity);
            foreach (NpgsqlParameter parameter in command.Parameters)
                parameter.NpgsqlDbType = NpgsqlDbType.Uuid;
        }
    }
}
