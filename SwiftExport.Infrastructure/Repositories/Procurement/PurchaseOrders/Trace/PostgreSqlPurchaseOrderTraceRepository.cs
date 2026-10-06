using System.Data;
using Dapper;
using Npgsql;
using NpgsqlTypes;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrders.Trace;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Core.Entities.Auditing;
using SwiftExport.Core.Entities.DemandPlanning;
using SwiftExport.Core.Entities.Inventory;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Entities.Procurement;
using SwiftExport.Core.Interfaces.Procurement;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;
using SwiftExport.Infrastructure.Repositories.Inventory.Inbounds;

namespace SwiftExport.Infrastructure.Repositories.Procurement.PurchaseOrders.Trace;

/// <summary>读取采购全部历史证据；起始历史行范围仅供整入库头门禁，后续六结果独立查询。</summary>
public sealed class PostgreSqlPurchaseOrderTraceRepository(RepositoryBaseContext context):IPurchaseOrderTraceRepository
{
    private static readonly EntityMetadata HeadMap=PostgreSqlEntityMetadata.GetMetadata(typeof(PurchaseOrderHead));
    private static readonly EntityMetadata LineMap=PostgreSqlEntityMetadata.GetMetadata(typeof(PurchaseOrderLine));
    private static readonly EntityMetadata ReportMap=PostgreSqlEntityMetadata.GetMetadata(typeof(SupplierProgressReport));
    private static readonly EntityMetadata LinkMap=PostgreSqlEntityMetadata.GetMetadata(typeof(DemandExecutionLink));
    private static readonly EntityMetadata NetMap=PostgreSqlEntityMetadata.GetMetadata(typeof(NetDemand));
    private static readonly EntityMetadata GrossMap=PostgreSqlEntityMetadata.GetMetadata(typeof(GrossDemand));
    private static readonly EntityMetadata ProductMap=PostgreSqlEntityMetadata.GetMetadata(typeof(Product));
    private static readonly EntityMetadata ReceiptHeadMap=PostgreSqlEntityMetadata.GetMetadata(typeof(InboundDocument));
    private static readonly EntityMetadata ReceiptLineMap=PostgreSqlEntityMetadata.GetMetadata(typeof(InboundDocumentLine));
    private static readonly EntityMetadata LogMap=PostgreSqlEntityMetadata.GetMetadata(typeof(BusinessOperationLog));

    /// <summary>同会话借用可空事务，一次scope、ScopedGuard及六结果；空头早返释放，取消和技术异常原样上交。</summary>
    public async Task<PurchaseOrderTraceDto?> GetAsync(Guid purchaseOrderId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var parameters=new PostgreSqlParameters();
        parameters.Add("PurchaseOrderId",purchaseOrderId);
        parameters.Add("PurchaseReceipt",(int)InboundDocumentTypeEnum.PurchaseReceipt);
        parameters.Add("FormalPostingStatuses",new[]{(int)InboundPostingStatusEnum.Posted,(int)InboundPostingStatusEnum.Reversed});
        // 已删采购行也定位整入库头；scope仅供门禁，不冻结后续六阶段证据。
        var scope=(await context.Session.Connection.QueryAsync<Guid>(new CommandDefinition(
            $"SELECT {C(LineMap,"scopeLine","Uid")} AS \"PurchaseLineId\" FROM {Table(LineMap)} scopeLine WHERE {C(LineMap,"scopeLine","PurchaseOrderId")}=@PurchaseOrderId;",
            new TraceParameters(parameters),context.Session.Transaction,cancellationToken:cancellationToken))).ToList();
        await PostgreSqlInboundReceiptHistoryGuard.ValidateScopedAsync(context.Session,scope,cancellationToken);
        using var grid=await context.Session.Connection.QueryMultipleAsync(new CommandDefinition(
            TraceSql(),new TraceParameters(parameters),context.Session.Transaction,cancellationToken:cancellationToken));
        var trace = await grid.ReadSingleOrDefaultAsync<PurchaseOrderTraceDto>();
        if (trace is null) return null;

        var lines = (await grid.ReadAsync<PurchaseOrderTraceLineDto>()).ToList();
        var links = (await grid.ReadAsync<PurchaseOrderTraceLinkDto>()).ToList();
        var reports = (await grid.ReadAsync<PurchaseOrderTraceProgressDto>()).ToList();
        var receipts = (await grid.ReadAsync<PurchaseOrderTraceReceiptDto>()).ToList();
        trace.Operations = (await grid.ReadAsync<PurchaseOrderTraceOperationDto>()).ToList();

        foreach (var line in lines)
        {
            line.Links = links.Where(x => x.PurchaseOrderLineId == line.PurchaseOrderLineId).ToList();
            line.ProgressHistory = reports.Where(x => x.PurchaseOrderLineId == line.PurchaseOrderLineId).ToList();
            line.Receipts = receipts.Where(x => x.PurchaseOrderLineId == line.PurchaseOrderLineId).ToList();
            // OriginKind 只记录创建类别；独立采购可后续补关联，按实际 Link 核实来源证据。
            if (trace.OriginKind != 2 && line.Links.Count == 0 && !line.IsDeleted)
                line.ReviewReason = "当前采购行未找到需求 Link，请核实来源。";
            else if (line.Links.Any(x => x.NeedsReview))
                line.ReviewReason = "部分需求来源身份、版本、指纹或贡献数量需核实。";
            // 原.NET空白和逐字符比较不能换成SQL字典规则；不一致保留未知收货量NULL。
            if (line.Receipts.Any(x =>
                (string.IsNullOrWhiteSpace(x.UnitSnapshot) || x.UnitSnapshot != line.UnitSnapshot)))
                line.ReviewReason = "正式入库单位与采购行不一致，净收货量需核实。";
            else
                line.FormalReceivedQty = line.Receipts.Sum(x => x.NetQuantity);
        }
        trace.Lines = lines;
        return trace;
    }

    /// <summary>独立读取当前历史集合；整行最新反馈不由稍后History覆盖，理由CASE及SQL三值逻辑沿源顺序。</summary>
    private static string TraceSql()=>$"""
        SELECT {C(HeadMap,"h","Uid")} AS "PurchaseOrderId",
                       {C(HeadMap,"h","OrderNo")} AS "OrderNo",
                       {C(HeadMap,"h","SupplierName")} AS "SupplierName",
                       {C(HeadMap,"h","OriginKind")} AS "OriginKind",
                       {C(HeadMap,"h","ConfirmationStatus")} AS "ConfirmationStatus",
                       {C(HeadMap,"h","LifecycleStatus")} AS "LifecycleStatus"
                FROM {Table(HeadMap)} h
                WHERE {C(HeadMap,"h","Uid")}=@PurchaseOrderId AND COALESCE({C(HeadMap,"h","IsDeleted")},FALSE)=FALSE;

        SELECT {C(LineMap,"l","Uid")} AS "PurchaseOrderLineId",
                       {C(LineMap,"l","LineNumber")} AS "LineNumber",
                       {C(LineMap,"l","IsDeleted")} AS "IsDeleted",
                       {C(LineMap,"l","Sku")} AS "Sku",
                       {C(LineMap,"l","UnitSnapshot")} AS "UnitSnapshot",
                       {C(LineMap,"l","Quantity")} AS "Quantity",
                       {C(LineMap,"l","UnlinkedQty")} AS "UnlinkedQty",
                       {C(LineMap,"l","PlannedDeliveryDate")} AS "PlannedDeliveryDate",
                       latest."ReadyQty" AS "LatestReadyQty"
                FROM {Table(LineMap)} l
                LEFT JOIN LATERAL
                (
                    SELECT {C(ReportMap,"f","ReadyQty")} AS "ReadyQty" FROM {Table(ReportMap)} f
                    WHERE {C(ReportMap,"f","PurchaseOrderLineId")}={C(LineMap,"l","Uid")} AND COALESCE({C(ReportMap,"f","IsDeleted")},FALSE)=FALSE
                    ORDER BY {C(ReportMap,"f","Sequence")} DESC NULLS LAST,{UuidOrder(C(ReportMap,"f","Uid"))} DESC NULLS LAST
                    LIMIT 1
                ) latest ON TRUE
                WHERE {C(LineMap,"l","PurchaseOrderId")}=@PurchaseOrderId
                ORDER BY {C(LineMap,"l","LineNumber")} ASC NULLS FIRST,{UuidOrder(C(LineMap,"l","Uid"))} ASC NULLS FIRST;

        SELECT {C(LinkMap,"link","Uid")} AS "LinkId",
                       {C(LinkMap,"link","TargetLineId")} AS "PurchaseOrderLineId",
                       {C(LinkMap,"link","NetDemandId")} AS "NetDemandId",
                       {C(LinkMap,"link","IsDeleted")} AS "IsDeleted",
                       {C(NetMap,"n","GrossDemandId")} AS "GrossDemandId",
                       {C(LinkMap,"link","LinkStatus")} AS "LinkStatus",
                       {C(LinkMap,"link","AllocatedQty")} AS "AllocatedQty",
                       {C(LinkMap,"link","ReleasedQty")} AS "ReleasedQty",
                       {C(LinkMap,"link","AllocatedQty")}-{C(LinkMap,"link","ReleasedQty")} AS "EffectiveQty",
                       {C(GrossMap,"g","SourceType")} AS "SourceType",
                       {C(GrossMap,"g","SourceUid")} AS "SourceUid",
                       {C(GrossMap,"g","SourceLineId")} AS "SourceLineId",
                       {C(GrossMap,"g","SourceNo")} AS "SourceNo",
                       {C(GrossMap,"g","SourceLineNumber")} AS "SourceLineNumber",
                       {C(NetMap,"n","DecompositionRevision")} AS "DecompositionRevision",
                       {C(GrossMap,"g","SourceVersion")} AS "SourceVersion",
                       {C(NetMap,"n","SourceDemandVersionSnapshot")} AS "SourceDemandVersionSnapshot",
                       {C(NetMap,"n","SourceVersionSnapshot")} AS "SourceVersionSnapshot",
                       {C(GrossMap,"g","DemandBasisFingerprint")} AS "DemandBasisFingerprint",
                       {C(NetMap,"n","SourceDemandBasisFingerprintSnapshot")} AS "DemandBasisFingerprintSnapshot",
                       {C(GrossMap,"g","UnitSnapshot")} AS "UnitSnapshot",
                       {C(LinkMap,"link","DemandRequiredDate")} AS "DemandRequiredDate",
                       {C(LinkMap,"link","CancelledAt")} AS "CancelledAt",
                       {C(LinkMap,"link","CancelledBy")} AS "CancelledBy",
                       {C(LinkMap,"link","CancelledReason")} AS "CancelledReason",
                       CASE WHEN {C(NetMap,"n","Uid")} IS NULL OR {C(GrossMap,"g","Uid")} IS NULL
                           OR {C(GrossMap,"g","SourceUid")} IS NULL OR {C(GrossMap,"g","SourceUid")}='00000000-0000-0000-0000-000000000000'
                           OR {C(GrossMap,"g","SourceLineId")} IS NULL OR {C(GrossMap,"g","SourceLineId")}='00000000-0000-0000-0000-000000000000'
                           OR {C(NetMap,"n","SourceDemandVersionSnapshot")} IS NULL OR {C(NetMap,"n","SourceDemandVersionSnapshot")}<>{C(GrossMap,"g","Version")}
                           OR (rtrim({C(GrossMap,"g","SourceType")},' ') COLLATE public.erp_dictionary_ci='MD' AND ({C(GrossMap,"g","SourceVersion")} IS NOT NULL OR {C(NetMap,"n","SourceVersionSnapshot")} IS NOT NULL))
                           OR (rtrim({C(GrossMap,"g","SourceType")},' ') COLLATE public.erp_dictionary_ci<>'MD' AND ({C(GrossMap,"g","SourceVersion")} IS NULL OR {C(NetMap,"n","SourceVersionSnapshot")} IS NULL
                               OR {C(NetMap,"n","SourceVersionSnapshot")}<>{C(GrossMap,"g","SourceVersion")}))
                           OR NULLIF(rtrim({C(GrossMap,"g","DemandBasisFingerprint")},' ') COLLATE public.erp_dictionary_ci,'') IS NULL
                           OR NULLIF(rtrim({C(NetMap,"n","SourceDemandBasisFingerprintSnapshot")},' ') COLLATE public.erp_dictionary_ci,'') IS NULL
                           OR rtrim({C(NetMap,"n","SourceDemandBasisFingerprintSnapshot")},' ') COLLATE public.erp_dictionary_ci<>rtrim({C(GrossMap,"g","DemandBasisFingerprint")},' ') COLLATE public.erp_dictionary_ci
                           OR NULLIF(btrim({C(LineMap,"l","UnitSnapshot")},' ') COLLATE public.erp_dictionary_ci,'') IS NULL
                           OR COALESCE(NULLIF(btrim({C(ProductMap,"p","Unit")},' ') COLLATE public.erp_dictionary_ci,''),
                               CASE WHEN {C(NetMap,"n","ParentNetDemandId")} IS NULL THEN NULLIF(btrim({C(GrossMap,"g","UnitSnapshot")},' ') COLLATE public.erp_dictionary_ci,'') END) IS NULL
                           OR btrim({C(LineMap,"l","UnitSnapshot")},' ') COLLATE public.erp_dictionary_ci<>COALESCE(NULLIF(btrim({C(ProductMap,"p","Unit")},' ') COLLATE public.erp_dictionary_ci,''),
                               CASE WHEN {C(NetMap,"n","ParentNetDemandId")} IS NULL THEN NULLIF(btrim({C(GrossMap,"g","UnitSnapshot")},' ') COLLATE public.erp_dictionary_ci,'') END)
                           OR {C(LinkMap,"link","TargetHeadId")}<>{C(LineMap,"l","PurchaseOrderId")}
                           OR {C(LinkMap,"link","AllocatedQty")}<0 OR {C(LinkMap,"link","ReleasedQty")}<0 OR {C(LinkMap,"link","ReleasedQty")}>{C(LinkMap,"link","AllocatedQty")}
                           THEN TRUE ELSE FALSE END AS "NeedsReview",
                       CASE WHEN {C(NetMap,"n","Uid")} IS NULL OR {C(GrossMap,"g","Uid")} IS NULL THEN '需求来源记录缺失。'
                            WHEN {C(GrossMap,"g","SourceUid")} IS NULL OR {C(GrossMap,"g","SourceUid")}='00000000-0000-0000-0000-000000000000'
                              OR {C(GrossMap,"g","SourceLineId")} IS NULL OR {C(GrossMap,"g","SourceLineId")}='00000000-0000-0000-0000-000000000000'
                              THEN '需求来源身份缺失。'
                            WHEN {C(NetMap,"n","SourceDemandVersionSnapshot")} IS NULL OR {C(NetMap,"n","SourceDemandVersionSnapshot")}<>{C(GrossMap,"g","Version")}
                              OR (rtrim({C(GrossMap,"g","SourceType")},' ') COLLATE public.erp_dictionary_ci='MD' AND ({C(GrossMap,"g","SourceVersion")} IS NOT NULL OR {C(NetMap,"n","SourceVersionSnapshot")} IS NOT NULL))
                              OR (rtrim({C(GrossMap,"g","SourceType")},' ') COLLATE public.erp_dictionary_ci<>'MD' AND ({C(GrossMap,"g","SourceVersion")} IS NULL OR {C(NetMap,"n","SourceVersionSnapshot")} IS NULL
                                  OR {C(NetMap,"n","SourceVersionSnapshot")}<>{C(GrossMap,"g","SourceVersion")}))
                              OR NULLIF(rtrim({C(GrossMap,"g","DemandBasisFingerprint")},' ') COLLATE public.erp_dictionary_ci,'') IS NULL
                              OR NULLIF(rtrim({C(NetMap,"n","SourceDemandBasisFingerprintSnapshot")},' ') COLLATE public.erp_dictionary_ci,'') IS NULL
                              OR rtrim({C(NetMap,"n","SourceDemandBasisFingerprintSnapshot")},' ') COLLATE public.erp_dictionary_ci<>rtrim({C(GrossMap,"g","DemandBasisFingerprint")},' ') COLLATE public.erp_dictionary_ci
                              THEN '来源版本或需求指纹缺失，或与当前来源不一致。'
                            WHEN NULLIF(btrim({C(LineMap,"l","UnitSnapshot")},' ') COLLATE public.erp_dictionary_ci,'') IS NULL
                              OR COALESCE(NULLIF(btrim({C(ProductMap,"p","Unit")},' ') COLLATE public.erp_dictionary_ci,''),
                                  CASE WHEN {C(NetMap,"n","ParentNetDemandId")} IS NULL THEN NULLIF(btrim({C(GrossMap,"g","UnitSnapshot")},' ') COLLATE public.erp_dictionary_ci,'') END) IS NULL
                              OR btrim({C(LineMap,"l","UnitSnapshot")},' ') COLLATE public.erp_dictionary_ci<>COALESCE(NULLIF(btrim({C(ProductMap,"p","Unit")},' ') COLLATE public.erp_dictionary_ci,''),
                                  CASE WHEN {C(NetMap,"n","ParentNetDemandId")} IS NULL THEN NULLIF(btrim({C(GrossMap,"g","UnitSnapshot")},' ') COLLATE public.erp_dictionary_ci,'') END)
                              THEN '物料与采购行单位不一致。'
                            WHEN {C(LinkMap,"link","TargetHeadId")}<>{C(LineMap,"l","PurchaseOrderId")} THEN 'Link 采购头行身份不一致。'
                            WHEN {C(LinkMap,"link","AllocatedQty")}<0 OR {C(LinkMap,"link","ReleasedQty")}<0 OR {C(LinkMap,"link","ReleasedQty")}>{C(LinkMap,"link","AllocatedQty")}
                              THEN '需求贡献或释放数量异常。'
                            ELSE NULL END AS "ReviewReason"
                FROM {Table(LinkMap)} link
                INNER JOIN {Table(LineMap)} l ON {C(LineMap,"l","Uid")}={C(LinkMap,"link","TargetLineId")} AND {C(LineMap,"l","PurchaseOrderId")}=@PurchaseOrderId
                LEFT JOIN {Table(NetMap)} n ON {C(NetMap,"n","Uid")}={C(LinkMap,"link","NetDemandId")}
                LEFT JOIN {Table(GrossMap)} g ON {C(GrossMap,"g","Uid")}={C(NetMap,"n","GrossDemandId")}
                LEFT JOIN {Table(ProductMap)} p ON {C(ProductMap,"p","Uid")}={C(NetMap,"n","ProductId")} AND {C(ProductMap,"p","IsDeleted")}=FALSE
                WHERE rtrim({C(LinkMap,"link","TargetType")},' ') COLLATE public.erp_dictionary_ci='PO'
                ORDER BY {C(LineMap,"l","LineNumber")} ASC NULLS FIRST,{C(LinkMap,"link","DemandRequiredDate")} ASC NULLS FIRST,{UuidOrder(C(LinkMap,"link","Uid"))} ASC NULLS FIRST;

        SELECT {C(ReportMap,"f","Uid")} AS "Uid",
                       {C(ReportMap,"f","PurchaseOrderLineId")} AS "PurchaseOrderLineId",
                       {C(ReportMap,"f","Sequence")} AS "Sequence",
                       {C(ReportMap,"f","ArrangementStatus")} AS "ArrangementStatus",
                       {C(ReportMap,"f","ReadyQty")} AS "ReadyQty",
                       {C(ReportMap,"f","ExpectedReadyDate")} AS "ExpectedReadyDate",
                       {C(ReportMap,"f","SupplierReportedDate")} AS "SupplierReportedDate",
                       {C(ReportMap,"f","Note")} AS "Note",
                       {C(ReportMap,"f","IsCorrection")} AS "IsCorrection",
                       {C(ReportMap,"f","CorrectionReason")} AS "CorrectionReason",
                       {C(ReportMap,"f","CreatedDate")} AS "CreatedDate",
                       {C(ReportMap,"f","CreatedBy")} AS "CreatedBy"
                FROM {Table(ReportMap)} f
                INNER JOIN {Table(LineMap)} l ON {C(LineMap,"l","Uid")}={C(ReportMap,"f","PurchaseOrderLineId")} AND {C(LineMap,"l","PurchaseOrderId")}=@PurchaseOrderId
                WHERE COALESCE({C(ReportMap,"f","IsDeleted")},FALSE)=FALSE
                ORDER BY {C(LineMap,"l","LineNumber")} ASC NULLS FIRST,{C(ReportMap,"f","Sequence")} DESC NULLS LAST,{UuidOrder(C(ReportMap,"f","Uid"))} DESC NULLS LAST;

        SELECT {C(ReceiptHeadMap,"h","Uid")} AS "InboundDocumentId",
                       {C(ReceiptLineMap,"l","Uid")} AS "InboundLineId",
                       {C(ReceiptLineMap,"l","SourceLineId")} AS "PurchaseOrderLineId",
                       {C(ReceiptHeadMap,"h","DocumentNo")} AS "DocumentNo",
                       {C(ReceiptHeadMap,"h","PostingStatus")} AS "PostingStatus",
                       {C(ReceiptLineMap,"l","Quantity")} AS "Quantity",
                       CASE WHEN {C(ReceiptHeadMap,"h","PostingStatus")}=1 THEN {C(ReceiptLineMap,"l","Quantity")} ELSE 0 END AS "NetQuantity",
                       {C(ReceiptLineMap,"l","UnitSnapshot")} AS "UnitSnapshot",
                       {C(ReceiptHeadMap,"h","WarehouseNameSnapshot")} AS "WarehouseNameSnapshot",
                       {C(ReceiptHeadMap,"h","ReceiptDate")} AS "ReceiptDate",
                       {C(ReceiptHeadMap,"h","PostedDate")} AS "PostedDate",
                       {C(ReceiptHeadMap,"h","PostedBy")} AS "PostedBy",
                       {C(ReceiptHeadMap,"h","ReversedDate")} AS "ReversedDate",
                       {C(ReceiptHeadMap,"h","ReversedBy")} AS "ReversedBy",
                       {C(ReceiptHeadMap,"h","ReverseReason")} AS "ReverseReason",
                       {C(ReceiptHeadMap,"h","TriggerPackingListId")} AS "TriggerPackingListId"
                FROM {Table(ReceiptLineMap)} l
                INNER JOIN {Table(ReceiptHeadMap)} h ON {C(ReceiptHeadMap,"h","Uid")}={C(ReceiptLineMap,"l","InboundDocumentId")}
                INNER JOIN {Table(LineMap)} p ON {C(LineMap,"p","Uid")}={C(ReceiptLineMap,"l","SourceLineId")} AND {C(LineMap,"p","PurchaseOrderId")}=@PurchaseOrderId
                WHERE {C(ReceiptHeadMap,"h","DocumentType")}=@PurchaseReceipt AND {C(ReceiptHeadMap,"h","PostingStatus")}=ANY(@FormalPostingStatuses)
                  AND COALESCE({C(ReceiptHeadMap,"h","IsDeleted")},FALSE)=FALSE AND COALESCE({C(ReceiptLineMap,"l","IsDeleted")},FALSE)=FALSE
                ORDER BY {C(ReceiptHeadMap,"h","ReceiptDate")} ASC NULLS FIRST,{UuidOrder(C(ReceiptHeadMap,"h","Uid"))} ASC NULLS FIRST,{C(ReceiptLineMap,"l","LineNumber")} ASC NULLS FIRST,{UuidOrder(C(ReceiptLineMap,"l","Uid"))} ASC NULLS FIRST;

        SELECT {C(LogMap,"log","Uid")} AS "Uid",
                       {C(LogMap,"log","OperationName")} AS "OperationName",
                       {C(LogMap,"log","OperationType")} AS "OperationType",
                       {C(LogMap,"log","Reason")} AS "Reason",
                       {C(LogMap,"log","Summary")} AS "Summary",
                       {C(LogMap,"log","UserNameSnapshot")} AS "UserName",
                       {C(LogMap,"log","OccurredDate")} AS "OccurredDate"
                FROM {Table(LogMap)} log
                WHERE {C(LogMap,"log","AggregateUid")}=@PurchaseOrderId AND COALESCE({C(LogMap,"log","IsDeleted")},FALSE)=FALSE
                ORDER BY {C(LogMap,"log","OccurredDate")} DESC NULLS LAST,{UuidOrder(C(LogMap,"log","Uid"))} DESC NULLS LAST;
        """;

    /// <summary>仅本类固定元数据和别名参与列拼接，不接受外部标识符。</summary>
    private static string C(EntityMetadata map,string alias,string property)=>alias+"."+PostgreSqlNames.Quote(map.Columns.Single(x=>x.PropertyName==property).ColumnName);
    /// <summary>可信映射提供完整schema/table并引用标识符。</summary>
    private static string Table(EntityMetadata map)=>PostgreSqlNames.Quote(map.SchemaName)+"."+PostgreSqlNames.Quote(map.TableName);
    /// <summary>保留SQL Server uniqueidentifier的SqlGuid字节比较顺序。</summary>
    private static string UuidOrder(string column)=>"ARRAY["+string.Join(',',new[]{10,11,12,13,14,15,8,9,7,6,5,4,3,2,1,0}.Select(x=>$"get_byte(uuid_send({column}),{x})"))+"]";

    /// <summary>独立克隆命令参数，明确uuid及integer数组，不修改共享参数或事务组件。</summary>
    private sealed class TraceParameters(PostgreSqlParameters parameters):SqlMapper.IDynamicParameters
    {
        /// <summary>明确驱动类型，避免装饰连接使Dapper展开数组。</summary>
        public void AddParameters(IDbCommand command,SqlMapper.Identity identity)
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
