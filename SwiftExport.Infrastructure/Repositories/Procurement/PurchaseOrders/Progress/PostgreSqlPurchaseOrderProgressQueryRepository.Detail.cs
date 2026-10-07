using Dapper;
using Npgsql;
using NpgsqlTypes;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderProgress;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Core.Entities.Inventory;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces.Procurement;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;
using SwiftExport.Infrastructure.Repositories.Inventory.Inbounds;

namespace SwiftExport.Infrastructure.Repositories.Procurement.PurchaseOrders.Progress;

/// <summary>采购进度详情沿源三个结果阶段读取；与已交付Search组成原接口，不接运行DI。</summary>
public sealed partial class PostgreSqlPurchaseOrderProgressQueryRepository : IPurchaseOrderProgressQueryRepository
{

    /// <summary>全局历史门禁后一次读取exists/当前行/完整报告；空头立即返回并释放grid，反馈以原.NET Guid顺序组装。</summary>
    public async Task<PurchaseOrderProgressDetailDto?> GetDetailAsync(Guid purchaseOrderId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var parameters = new PostgreSqlParameters();
        parameters.Add("PurchaseOrderId", purchaseOrderId);
        parameters.Add("PurchaseReceipt", (int)InboundDocumentTypeEnum.PurchaseReceipt);
        parameters.Add("FormalPostingStatuses", new[] { (int)InboundPostingStatusEnum.Posted, (int)InboundPostingStatusEnum.Reversed });
        await PostgreSqlInboundReceiptHistoryGuard.ValidateGlobalAsync(context.Session, cancellationToken);
        using var grid = await context.Session.Connection.QueryMultipleAsync(new CommandDefinition(
            DetailSql(), new DetailParameters(parameters), context.Session.Transaction, cancellationToken: cancellationToken));
        var exists = await grid.ReadSingleAsync<bool>();
        if (!exists) return null;
        var lines = (await grid.ReadAsync<PurchaseOrderProgressLineDto>()).ToList();
        var reports = (await grid.ReadAsync<SupplierProgressReportDto>()).ToList();
        foreach (var line in lines)
        {
            // SQL反馈顺序只是当前批次读取序；源最终History/Latest用.NET Guid比较，不改为SqlGuid。
            line.ProgressHistory = reports.Where(report => report.PurchaseOrderLineId == line.PurchaseOrderLineId)
                .OrderByDescending(report => report.Sequence).ThenByDescending(report => report.Uid).ToList();
            line.LatestProgress = line.ProgressHistory.FirstOrDefault();
        }
        return new() { PurchaseOrderId = purchaseOrderId, Lines = lines };
    }

    /// <summary>三个语句独立读取当阶段当前事实；报告只复核有效行，不冻结第一阶段行身份或新增头有效性复验。</summary>
    private static string DetailSql() => $"""
        SELECT EXISTS(SELECT 1 FROM "public"."purchase_order_head" h
            WHERE h."uid"=@PurchaseOrderId AND COALESCE(h."is_deleted",FALSE)=FALSE);

        WITH ReceiptFacts AS
        (
            SELECT receiveLine."source_line_id" AS source_line_id,
                SUM(CASE WHEN receiveHead."posting_status"=1
                    THEN receiveLine."quantity" ELSE 0 END) AS received_qty
            FROM "public"."inbound_document_line" receiveLine
            INNER JOIN "public"."inbound_document" receiveHead ON receiveHead."uid"=receiveLine."inbound_document_id"
            WHERE receiveHead."document_type"=@PurchaseReceipt
                AND receiveHead."posting_status"=ANY(@FormalPostingStatuses)
                AND COALESCE(receiveLine."is_deleted",FALSE)=FALSE
                AND COALESCE(receiveHead."is_deleted",FALSE)=FALSE
            GROUP BY receiveLine."source_line_id"
        )
        SELECT l."uid" AS "PurchaseOrderLineId",l."version" AS "Version",l."line_number" AS "LineNumber",
            COALESCE(product."main_image",'') AS "MainImage",
            l."sku" AS "Sku",l."supplier_sku" AS "SupplierSku",l."description_zh" AS "DescriptionZh",
            l."quantity" AS "Quantity",COALESCE(receipt.received_qty,0) AS "ReceivedQty",
            l."planned_delivery_date" AS "PlannedDeliveryDate"
        FROM "public"."purchase_order_line" l
        INNER JOIN "public"."purchase_order_head" h ON h."uid"=l."purchase_order_id" AND COALESCE(h."is_deleted",FALSE)=FALSE
        LEFT JOIN "public"."product" product ON product."uid"=l."product_id"
            AND COALESCE(product."is_deleted",FALSE)=FALSE
        LEFT JOIN ReceiptFacts receipt ON receipt.source_line_id=l."uid"
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

    /// <summary>本详情局部明确uuid、int[]及integer；沿共享克隆，不修改公共参数或驱动推断。</summary>
    private sealed class DetailParameters(PostgreSqlParameters parameters) : SqlMapper.IDynamicParameters
    {
        /// <summary>参数独立归命令所有，数组不展开，查询事务与连接仍由调用者拥有。</summary>
        public void AddParameters(System.Data.IDbCommand command, SqlMapper.Identity identity)
        {
            parameters.AddParameters(command, identity);
            foreach (NpgsqlParameter parameter in command.Parameters)
                parameter.NpgsqlDbType = parameter.ParameterName switch
                {
                    "PurchaseOrderId" => NpgsqlDbType.Uuid,
                    "FormalPostingStatuses" => NpgsqlDbType.Array | NpgsqlDbType.Integer,
                    _ => NpgsqlDbType.Integer
                };
        }
    }
}
