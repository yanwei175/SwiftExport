using Dapper;
using Npgsql;
using NpgsqlTypes;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderProgress;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Procurement;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;
using SwiftExport.Infrastructure.Repositories.Inventory.Inbounds;

namespace SwiftExport.Infrastructure.Repositories.Procurement.PurchaseOrders.Progress;

/// <summary>采购进度列表PG投影；全局历史门禁后一次冻结商业事实，再仅按结构分页，详情另行实施。</summary>
public sealed partial class PostgreSqlPurchaseOrderProgressQueryRepository(RepositoryBaseContext context)
{
    private static readonly EntityMetadata HeadMap=PostgreSqlEntityMetadata.GetMetadata(typeof(PurchaseOrderHead));
    private static readonly EntityMetadata LineMap=PostgreSqlEntityMetadata.GetMetadata(typeof(PurchaseOrderLine));
    private static readonly EntityMetadata ReportMap=PostgreSqlEntityMetadata.GetMetadata(typeof(SupplierProgressReport));

    /// <summary>借用当前连接及可空事务，完整缓冲SQL排序后的HeadFacts；分页不重新观察头、行或反馈。</summary>
    public async Task<PagedResponseBase<PurchaseOrderProgressListItemDto>> SearchAsync(
        PurchaseOrderProgressSearchRequest request,CancellationToken cancellationToken=default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var parameters=new PostgreSqlParameters();
        foreach(var pair in new[]{("Keyword",request.Keyword),("OrderNo",request.OrderNo),("SupplierKeyword",request.SupplierKeyword),("Sku",request.Sku)})
            parameters.Add(pair.Item1,ToLikePattern(pair.Item2));
        AddStatuses(parameters,"Confirmation",request.ConfirmationStatuses.Select(x=>(int)x).ToArray());
        AddStatuses(parameters,"Lifecycle",request.LifecycleStatuses.Select(x=>(int)x).ToArray());
        AddStatuses(parameters,"Arrangement",request.ArrangementStatuses.Select(x=>(int)x).ToArray());
        parameters.AddTimestamp("PlannedDeliveryDateFrom",request.PlannedDeliveryDateFrom);
        parameters.AddTimestamp("PlannedDeliveryDateTo",request.PlannedDeliveryDateTo);
        parameters.AddTimestamp("ExpectedReadyDateFrom",request.ExpectedReadyDateFrom);
        parameters.AddTimestamp("ExpectedReadyDateTo",request.ExpectedReadyDateTo);
        parameters.Add("Active",(int)PurchaseOrderLifecycleStatusEnum.Active);
        parameters.Add("Confirmed",(int)ConfirmationStatusEnum.已确认);
        var skip=(request.PageIndex-1)*request.PageSize;
        await PostgreSqlInboundReceiptHistoryGuard.ValidateGlobalAsync(context.Session,cancellationToken);
        var frozen=(await context.Session.Connection.QueryAsync<PurchaseOrderProgressListItemDto>(new CommandDefinition(
            SearchSql(),new SearchParameters(parameters),context.Session.Transaction,cancellationToken:cancellationToken))).ToList();
        cancellationToken.ThrowIfCancellationRequested();
        return new(){TotalCount=frozen.Count,Items=frozen.Skip(skip).Take(request.PageSize).ToList(),PageIndex=request.PageIndex,PageSize=request.PageSize};
    }

    /// <summary>空状态仍传源哨兵-1但关闭限制；枚举去重不增加新状态资格。</summary>
    private static void AddStatuses(PostgreSqlParameters parameters,string dimension,int[] values)
    {
        parameters.Add(dimension+"Statuses",values.Length==0?new[]{-1}:values.Distinct().ToArray());
        parameters.Add("Limit"+dimension+"Statuses",values.Length>0);
    }

    /// <summary>仅清理外部空白并转义限定LIKE字符，问号和内部空格保留字面。</summary>
    private static string? ToLikePattern(string? value)=>string.IsNullOrWhiteSpace(value)?null:
        "%"+value.Trim().Replace("~","~~").Replace("%","~%").Replace("_","~_").Replace("[","~[")+"%";

    /// <summary>全部商业筛选、整行最新反馈及汇总在一个SQL完成；输出已按源排序，不重查商业事实。</summary>
    private static string SearchSql()=>$"""
        WITH LineFacts AS MATERIALIZED
        (
            SELECT {L(nameof(PurchaseOrderLine.PurchaseOrderId))} AS purchase_order_id,{L(nameof(PurchaseOrderLine.Uid))} AS uid,
                {L(nameof(PurchaseOrderLine.Quantity))} AS quantity,{L(nameof(PurchaseOrderLine.PlannedDeliveryDate))} AS planned_delivery_date,
                latest.arrangement_status,latest.ready_qty,latest.expected_ready_date,
                CASE WHEN latest.sequence IS NULL THEN 0 ELSE 1 END AS has_progress
            FROM {Table(LineMap)} l
            LEFT JOIN LATERAL
            (
                SELECT {R(nameof(SupplierProgressReport.Sequence))} AS sequence,{R(nameof(SupplierProgressReport.ArrangementStatus))} AS arrangement_status,
                    {R(nameof(SupplierProgressReport.ReadyQty))} AS ready_qty,{R(nameof(SupplierProgressReport.ExpectedReadyDate))} AS expected_ready_date
                FROM {Table(ReportMap)} r
                WHERE {R(nameof(SupplierProgressReport.PurchaseOrderLineId))}={L(nameof(PurchaseOrderLine.Uid))}
                    AND COALESCE({R(nameof(SupplierProgressReport.IsDeleted))},FALSE)=FALSE
                ORDER BY {R(nameof(SupplierProgressReport.Sequence))} DESC NULLS LAST,{UuidOrder(R(nameof(SupplierProgressReport.Uid)))} DESC LIMIT 1
            ) latest ON TRUE
            WHERE COALESCE({L(nameof(PurchaseOrderLine.IsDeleted))},FALSE)=FALSE
        ), HeadFacts AS
        (
            SELECT {H(nameof(PurchaseOrderHead.Uid))} AS "PurchaseOrderId",{H(nameof(PurchaseOrderHead.Version))} AS "Version",
                {H(nameof(PurchaseOrderHead.ConfirmationStatus))} AS "ConfirmationStatus",{H(nameof(PurchaseOrderHead.LifecycleStatus))} AS "LifecycleStatus",
                {H(nameof(PurchaseOrderHead.OrderNo))} AS "OrderNo",{H(nameof(PurchaseOrderHead.OrderDate))} AS "OrderDate",
                {H(nameof(PurchaseOrderHead.BoNo))} AS "BoNo",{H(nameof(PurchaseOrderHead.SupplierName))} AS "SupplierName",
                {H(nameof(PurchaseOrderHead.DemandSourceNo))} AS "DemandSourceNo",
                COUNT(f.uid)::integer AS "LineCount",COALESCE(SUM(f.has_progress),0)::integer AS "ReportedLineCount",
                SUM(CASE WHEN f.uid IS NOT NULL AND f.ready_qty IS NULL THEN 1 ELSE 0 END)::integer AS "UnknownReadyQtyLineCount",
                COALESCE(SUM(f.quantity),0) AS "TotalQty",COALESCE(SUM(f.ready_qty),0) AS "LatestReadyQty",
                MAX(f.expected_ready_date) AS "LatestExpectedReadyDate"
            FROM {Table(HeadMap)} h LEFT JOIN LineFacts f ON f.purchase_order_id={H(nameof(PurchaseOrderHead.Uid))}
            WHERE COALESCE({H(nameof(PurchaseOrderHead.IsDeleted))},FALSE)=FALSE
                AND (@Keyword IS NULL OR {Like(H(nameof(PurchaseOrderHead.OrderNo)),"Keyword")} OR {Like(H(nameof(PurchaseOrderHead.BoNo)),"Keyword")}
                    OR {Like(H(nameof(PurchaseOrderHead.SupplierName)),"Keyword")} OR EXISTS
                    (SELECT 1 FROM {Table(LineMap)} keywordLine WHERE {C(LineMap,"keywordLine",nameof(PurchaseOrderLine.PurchaseOrderId))}={H(nameof(PurchaseOrderHead.Uid))}
                        AND COALESCE({C(LineMap,"keywordLine",nameof(PurchaseOrderLine.IsDeleted))},FALSE)=FALSE AND {Like(C(LineMap,"keywordLine",nameof(PurchaseOrderLine.Sku)),"Keyword")}))
                AND (@OrderNo IS NULL OR {Like(H(nameof(PurchaseOrderHead.OrderNo)),"OrderNo")})
                AND (@SupplierKeyword IS NULL OR {Like(H(nameof(PurchaseOrderHead.SupplierCode)),"SupplierKeyword")} OR {Like(H(nameof(PurchaseOrderHead.SupplierName)),"SupplierKeyword")})
                AND (@Sku IS NULL OR EXISTS(SELECT 1 FROM {Table(LineMap)} skuLine
                    WHERE {C(LineMap,"skuLine",nameof(PurchaseOrderLine.PurchaseOrderId))}={H(nameof(PurchaseOrderHead.Uid))}
                    AND COALESCE({C(LineMap,"skuLine",nameof(PurchaseOrderLine.IsDeleted))},FALSE)=FALSE AND {Like(C(LineMap,"skuLine",nameof(PurchaseOrderLine.Sku)),"Sku")}))
                AND (NOT @LimitConfirmationStatuses OR {H(nameof(PurchaseOrderHead.ConfirmationStatus))}=ANY(@ConfirmationStatuses))
                AND (NOT @LimitLifecycleStatuses OR {H(nameof(PurchaseOrderHead.LifecycleStatus))}=ANY(@LifecycleStatuses))
                AND (NOT @LimitArrangementStatuses OR EXISTS(SELECT 1 FROM LineFacts arrangementLine
                    WHERE arrangementLine.purchase_order_id={H(nameof(PurchaseOrderHead.Uid))}
                    AND COALESCE(arrangementLine.arrangement_status,0)=ANY(@ArrangementStatuses)))
                AND ((@PlannedDeliveryDateFrom IS NULL AND @PlannedDeliveryDateTo IS NULL) OR EXISTS
                    (SELECT 1 FROM {Table(LineMap)} plannedLine WHERE {C(LineMap,"plannedLine",nameof(PurchaseOrderLine.PurchaseOrderId))}={H(nameof(PurchaseOrderHead.Uid))}
                        AND COALESCE({C(LineMap,"plannedLine",nameof(PurchaseOrderLine.IsDeleted))},FALSE)=FALSE
                        AND (@PlannedDeliveryDateFrom IS NULL OR {C(LineMap,"plannedLine",nameof(PurchaseOrderLine.PlannedDeliveryDate))}>=@PlannedDeliveryDateFrom)
                        AND (@PlannedDeliveryDateTo IS NULL OR {C(LineMap,"plannedLine",nameof(PurchaseOrderLine.PlannedDeliveryDate))}<@PlannedDeliveryDateTo+INTERVAL '1 day')))
            GROUP BY {H(nameof(PurchaseOrderHead.Uid))},{H(nameof(PurchaseOrderHead.Version))},{H(nameof(PurchaseOrderHead.ConfirmationStatus))},{H(nameof(PurchaseOrderHead.LifecycleStatus))},
                {H(nameof(PurchaseOrderHead.OrderNo))},{H(nameof(PurchaseOrderHead.OrderDate))},{H(nameof(PurchaseOrderHead.BoNo))},{H(nameof(PurchaseOrderHead.SupplierName))},{H(nameof(PurchaseOrderHead.DemandSourceNo))}
        )
        SELECT facts."PurchaseOrderId" AS "PurchaseOrderId",facts."Version" AS "Version",facts."ConfirmationStatus" AS "ConfirmationStatus",
            facts."LifecycleStatus" AS "LifecycleStatus",facts."OrderNo" AS "OrderNo",facts."OrderDate" AS "OrderDate",facts."BoNo" AS "BoNo",
            facts."SupplierName" AS "SupplierName",facts."DemandSourceNo" AS "DemandSourceNo",facts."LineCount" AS "LineCount",
            facts."ReportedLineCount" AS "ReportedLineCount",facts."UnknownReadyQtyLineCount" AS "UnknownReadyQtyLineCount",
            facts."TotalQty" AS "TotalQty",facts."LatestReadyQty" AS "LatestReadyQty",facts."LatestExpectedReadyDate" AS "LatestExpectedReadyDate",
            CASE WHEN facts."LifecycleStatus"=@Active AND facts."ConfirmationStatus"=@Confirmed THEN TRUE ELSE FALSE END AS "CanReportProgress",
            CASE WHEN facts."LifecycleStatus"<>@Active THEN '仅活动采购单允许登记供应商反馈。'
                WHEN facts."ConfirmationStatus"<>@Confirmed THEN '仅已确认采购单允许登记供应商反馈。' ELSE '' END AS "ReadOnlyReason"
        FROM HeadFacts facts
        WHERE (@ExpectedReadyDateFrom IS NULL OR facts."LatestExpectedReadyDate">=@ExpectedReadyDateFrom)
            AND (@ExpectedReadyDateTo IS NULL OR facts."LatestExpectedReadyDate"<@ExpectedReadyDateTo+INTERVAL '1 day')
        ORDER BY facts."OrderDate" DESC NULLS LAST,facts."OrderNo" COLLATE public.erp_dictionary_ci DESC NULLS LAST,
            {UuidOrder("facts.\"PurchaseOrderId\"")} DESC
        """;

    /// <summary>固定SQL字面筛选沿既有字典比较，不扩展为通配搜索。</summary>
    private static string Like(string column,string parameter)=>column+" COLLATE public.erp_dictionary_ci LIKE @"+parameter+" ESCAPE '~'";
    /// <summary>只引用现有可信表元数据。</summary>
    private static string Table(EntityMetadata map)=>PostgreSqlNames.Quote(map.SchemaName)+"."+PostgreSqlNames.Quote(map.TableName);
    /// <summary>以CLR属性定位PG列，包括旧QTY对应quantity，不接受外部标识符。</summary>
    private static string C(EntityMetadata map,string alias,string property)=>alias+"."+PostgreSqlNames.Quote(map.Columns.Single(x=>x.PropertyName==property).ColumnName);
    private static string H(string property)=>C(HeadMap,"h",property);
    private static string L(string property)=>C(LineMap,"l",property);
    private static string R(string property)=>C(ReportMap,"r",property);
    /// <summary>保留SQL Server uniqueidentifier尾键顺序。</summary>
    private static string UuidOrder(string column)=>"ARRAY["+string.Join(',',new[]{10,11,12,13,14,15,8,9,7,6,5,4,3,2,1,0}.Select(x=>$"get_byte(uuid_send({column}),{x})"))+"]";

    /// <summary>仅给本查询明确nullable标量和int数组类型，不修改共享参数类或连接识别。</summary>
    private sealed class SearchParameters(PostgreSqlParameters parameters):SqlMapper.IDynamicParameters
    {
        /// <summary>克隆后的参数归命令所有；数组不由Dapper展开，时间按墙上时间绑定。</summary>
        public void AddParameters(System.Data.IDbCommand command,SqlMapper.Identity identity)
        {
            parameters.AddParameters(command,identity);
            foreach(NpgsqlParameter parameter in command.Parameters)
                parameter.NpgsqlDbType=parameter.ParameterName switch
                {
                    "ConfirmationStatuses" or "LifecycleStatuses" or "ArrangementStatuses"=>NpgsqlDbType.Array|NpgsqlDbType.Integer,
                    "PlannedDeliveryDateFrom" or "PlannedDeliveryDateTo" or "ExpectedReadyDateFrom" or "ExpectedReadyDateTo"=>NpgsqlDbType.Timestamp,
                    "Keyword" or "OrderNo" or "SupplierKeyword" or "Sku"=>NpgsqlDbType.Text,
                    "LimitConfirmationStatuses" or "LimitLifecycleStatuses" or "LimitArrangementStatuses"=>NpgsqlDbType.Boolean,
                    _=>NpgsqlDbType.Integer
                };
        }
    }
}
