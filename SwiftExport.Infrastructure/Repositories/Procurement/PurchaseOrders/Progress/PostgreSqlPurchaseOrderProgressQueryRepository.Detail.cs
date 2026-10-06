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
public sealed partial class PostgreSqlPurchaseOrderProgressQueryRepository:IPurchaseOrderProgressQueryRepository
{
    private static readonly EntityMetadata DetailProductMap=PostgreSqlEntityMetadata.GetMetadata(typeof(Product));
    private static readonly EntityMetadata ReceiptHeadMap=PostgreSqlEntityMetadata.GetMetadata(typeof(InboundDocument));
    private static readonly EntityMetadata ReceiptLineMap=PostgreSqlEntityMetadata.GetMetadata(typeof(InboundDocumentLine));

    /// <summary>全局历史门禁后一次读取exists/当前行/完整报告；空头立即返回并释放grid，反馈以原.NET Guid顺序组装。</summary>
    public async Task<PurchaseOrderProgressDetailDto?> GetDetailAsync(Guid purchaseOrderId,CancellationToken cancellationToken=default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var parameters=new PostgreSqlParameters();
        parameters.Add("PurchaseOrderId",purchaseOrderId);
        parameters.Add("PurchaseReceipt",(int)InboundDocumentTypeEnum.PurchaseReceipt);
        parameters.Add("FormalPostingStatuses",new[]{(int)InboundPostingStatusEnum.Posted,(int)InboundPostingStatusEnum.Reversed});
        await PostgreSqlInboundReceiptHistoryGuard.ValidateGlobalAsync(context.Session,cancellationToken);
        using var grid=await context.Session.Connection.QueryMultipleAsync(new CommandDefinition(
            DetailSql(),new DetailParameters(parameters),context.Session.Transaction,cancellationToken:cancellationToken));
        var exists=await grid.ReadSingleAsync<bool>();
        if(!exists)return null;
        var lines=(await grid.ReadAsync<PurchaseOrderProgressLineDto>()).ToList();
        var reports=(await grid.ReadAsync<SupplierProgressReportDto>()).ToList();
        foreach(var line in lines)
        {
            // SQL反馈顺序只是当前批次读取序；源最终History/Latest用.NET Guid比较，不改为SqlGuid。
            line.ProgressHistory=reports.Where(report=>report.PurchaseOrderLineId==line.PurchaseOrderLineId)
                .OrderByDescending(report=>report.Sequence).ThenByDescending(report=>report.Uid).ToList();
            line.LatestProgress=line.ProgressHistory.FirstOrDefault();
        }
        return new(){PurchaseOrderId=purchaseOrderId,Lines=lines};
    }

    /// <summary>三个语句独立读取当阶段当前事实；报告只复核有效行，不冻结第一阶段行身份或新增头有效性复验。</summary>
    private static string DetailSql()=>$"""
        SELECT EXISTS(SELECT 1 FROM {Table(HeadMap)} h
            WHERE {H("Uid")}=@PurchaseOrderId AND COALESCE({H("IsDeleted")},FALSE)=FALSE);

        WITH ReceiptFacts AS
        (
            SELECT {C(ReceiptLineMap,"receiveLine","SourceLineId")} AS source_line_id,
                SUM(CASE WHEN {C(ReceiptHeadMap,"receiveHead","PostingStatus")}=1
                    THEN {C(ReceiptLineMap,"receiveLine","Quantity")} ELSE 0 END) AS received_qty
            FROM {Table(ReceiptLineMap)} receiveLine
            INNER JOIN {Table(ReceiptHeadMap)} receiveHead ON {C(ReceiptHeadMap,"receiveHead","Uid")}={C(ReceiptLineMap,"receiveLine","InboundDocumentId")}
            WHERE {C(ReceiptHeadMap,"receiveHead","DocumentType")}=@PurchaseReceipt
                AND {C(ReceiptHeadMap,"receiveHead","PostingStatus")}=ANY(@FormalPostingStatuses)
                AND COALESCE({C(ReceiptLineMap,"receiveLine","IsDeleted")},FALSE)=FALSE
                AND COALESCE({C(ReceiptHeadMap,"receiveHead","IsDeleted")},FALSE)=FALSE
            GROUP BY {C(ReceiptLineMap,"receiveLine","SourceLineId")}
        )
        SELECT {L("Uid")} AS "PurchaseOrderLineId",{L("Version")} AS "Version",{L("LineNumber")} AS "LineNumber",
            COALESCE({C(DetailProductMap,"product","MainImage")},'') AS "MainImage",
            {L("Sku")} AS "Sku",{L("SupplierSku")} AS "SupplierSku",{L("DescriptionZh")} AS "DescriptionZh",
            {L("Quantity")} AS "Quantity",COALESCE(receipt.received_qty,0) AS "ReceivedQty",
            {L("PlannedDeliveryDate")} AS "PlannedDeliveryDate"
        FROM {Table(LineMap)} l
        INNER JOIN {Table(HeadMap)} h ON {H("Uid")}={L("PurchaseOrderId")} AND COALESCE({H("IsDeleted")},FALSE)=FALSE
        LEFT JOIN {Table(DetailProductMap)} product ON {C(DetailProductMap,"product","Uid")}={L("ProductId")}
            AND COALESCE({C(DetailProductMap,"product","IsDeleted")},FALSE)=FALSE
        LEFT JOIN ReceiptFacts receipt ON receipt.source_line_id={L("Uid")}
        WHERE {L("PurchaseOrderId")}=@PurchaseOrderId AND COALESCE({L("IsDeleted")},FALSE)=FALSE
        ORDER BY {L("LineNumber")} ASC NULLS FIRST,{UuidOrder(L("Uid"))} ASC;

        SELECT {R("Uid")} AS "Uid",{R("PurchaseOrderLineId")} AS "PurchaseOrderLineId",{R("Sequence")} AS "Sequence",
            {R("ArrangementStatus")} AS "ArrangementStatus",{R("ReadyQty")} AS "ReadyQty",
            {R("ExpectedReadyDate")} AS "ExpectedReadyDate",{R("SupplierReportedDate")} AS "SupplierReportedDate",
            {R("Note")} AS "Note",{R("IsCorrection")} AS "IsCorrection",{R("CorrectionReason")} AS "CorrectionReason",
            {R("CommandId")} AS "CommandId",{R("CreatedDate")} AS "CreatedDate",{R("CreatedBy")} AS "CreatedBy"
        FROM {Table(ReportMap)} r
        INNER JOIN {Table(LineMap)} l ON {L("Uid")}={R("PurchaseOrderLineId")}
            AND {L("PurchaseOrderId")}=@PurchaseOrderId AND COALESCE({L("IsDeleted")},FALSE)=FALSE
        WHERE COALESCE({R("IsDeleted")},FALSE)=FALSE
        ORDER BY {L("LineNumber")} ASC NULLS FIRST,{R("Sequence")} DESC NULLS LAST,{UuidOrder(R("Uid"))} DESC;
        """;

    /// <summary>本详情局部明确uuid、int[]及integer；沿共享克隆，不修改公共参数或驱动推断。</summary>
    private sealed class DetailParameters(PostgreSqlParameters parameters):SqlMapper.IDynamicParameters
    {
        /// <summary>参数独立归命令所有，数组不展开，查询事务与连接仍由调用者拥有。</summary>
        public void AddParameters(System.Data.IDbCommand command,SqlMapper.Identity identity)
        {
            parameters.AddParameters(command,identity);
            foreach(NpgsqlParameter parameter in command.Parameters)
                parameter.NpgsqlDbType=parameter.ParameterName switch
                {
                    "PurchaseOrderId"=>NpgsqlDbType.Uuid,
                    "FormalPostingStatuses"=>NpgsqlDbType.Array|NpgsqlDbType.Integer,
                    _=>NpgsqlDbType.Integer
                };
        }
    }
}
