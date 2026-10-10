using Dapper;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.SysEnums.Shipping;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Core.Entities.DemandPlanning;
using SwiftExport.Core.Entities.SalesOrders;
using SwiftExport.Core.Entities.Shipping;
using SwiftExport.Core.Interfaces.SalesOrders.Query;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;
using System.Text.Json;

namespace SwiftExport.Infrastructure.Repositories.SalesOrders.Query;

/// <summary>三个销售查询按源观察阶段借用PG请求会话；仓储接口已齐备，完整服务链与运行DI仍单独验收。</summary>
public sealed partial class PostgreSqlSalesOrderQueryRepository(RepositoryBaseContext context) : ISalesOrderQueryRepository
{
    private const string HeaderProjection = """
        "uid" AS "Uid",
        "version" AS "Version",
        "is_deleted" AS "IsDeleted",
        "created_date" AS "CreatedDate",
        "created_by" AS "CreatedBy",
        "updated_date" AS "UpdatedDate",
        "updated_by" AS "UpdatedBy",
        "document_status" AS "DocumentStatus",
        "confirmation_status" AS "ConfirmationStatus",
        "order_no" AS "OrderNo",
        "invoice_no" AS "InvoiceNo",
        "invoice_update_date" AS "InvoiceUpdateDate",
        "customer_id" AS "CustomerId",
        "customer_code" AS "CustomerCode",
        "customer_po_no" AS "CustomerPoNo",
        "shipped_by" AS "ShippedBy",
        "currency" AS "Currency",
        "exchange_rate" AS "ExchangeRate",
        "total_amount_original_currency" AS "TotalAmountOriginalCurrency",
        "total_amount_local_currency" AS "TotalAmountLocalCurrency",
        "ship_to_name" AS "ShipToName",
        "ship_to_address" AS "ShipToAddress",
        "ship_to_phone" AS "ShipToPhone",
        "incoterm" AS "Incoterm",
        "incoterm_location" AS "IncotermLocation",
        "incoterm_sort_remark" AS "IncotermSortRemark",
        "deposit_rate" AS "DepositRate",
        "lead_time_days" AS "LeadTimeDays",
        "payment_method" AS "PaymentMethod",
        "so_folder" AS "SoFolder",
        "remark" AS "Remark",
        "order_date" AS "OrderDate",
        "delivery_date" AS "DeliveryDate",
        "reply_date" AS "ReplyDate",
        "eta" AS "ETA",
        "etd" AS "ETD",
        "close_reason" AS "CloseReason",
        "closed_date" AS "ClosedDate",
        "closed_by" AS "ClosedBy"
    """;

    /// <summary>每个独立命令沿用可空事务与取消；不创建临时表、不重试、不接管事务或连接生命周期。</summary>
    private async Task<List<T>> ReadAsync<T>(string sql, PostgreSqlParameters parameters, CancellationToken token)
        => (await context.Session.Connection.QueryAsync<T>(new CommandDefinition(
            sql, parameters, transaction: context.Session.Transaction, cancellationToken: token))).ToList();

    /// <summary>将源SQL枚举显式绑定为整数；同一参数包可以在分阶段命令之间复用。</summary>
    private static PostgreSqlParameters CreateParameters(Guid uid)
    {
        var parameters = new PostgreSqlParameters();
        parameters.Add("Uid", uid);
        parameters.Add("SalesSourceType", DocumentTypeStaticsEnum.SalesOrder);
        parameters.Add("OrderOpen", (int)SalesOrderDocumentStatusEnum.Open);
        parameters.Add("OrderClosed", (int)SalesOrderDocumentStatusEnum.Closed);
        parameters.Add("Pending", (int)ConfirmationStatusEnum.待确认);
        parameters.Add("Confirmed", (int)ConfirmationStatusEnum.已确认);
        parameters.Add("DemandActive", (int)GrossDemandLifecycleStatusEnum.Active);
        parameters.Add("DemandVoided", (int)GrossDemandLifecycleStatusEnum.Voided);
        parameters.Add("Unposted", (int)PackingListPostingStatusEnum.Unposted);
        parameters.Add("Posted", (int)PackingListPostingStatusEnum.Posted);
        parameters.Add("Reversed", (int)PackingListPostingStatusEnum.Reversed);
        return parameters;
    }

    /// <summary>先冻结源候选头七字段；后续读取真实头行时不刷新本关系。</summary>
    private async Task FreezeCandidatesAsync(PostgreSqlParameters parameters, CancellationToken token)
    {
        var candidates = await ReadAsync<CandidateOrder>($"""
            SELECT h.uid AS "Uid",h.version AS "Version",h.document_status AS "DocumentStatus",
                   h.confirmation_status AS "ConfirmationStatus",h.customer_id AS "CustomerId",
                   h.order_no AS "OrderNo",h.delivery_date AS "DeliveryDate"
            FROM "public"."sales_order_header" h WHERE h.uid=@Uid AND h.is_deleted=FALSE
            """, parameters, token);
        parameters.Add("Candidates", JsonSerializer.Serialize(candidates));
    }

    /// <summary>每次只执行一条行事实SQL并完整缓冲；C#仅传递已物化记录，不计算商业数量。</summary>
    private async Task FreezeLineFactsAsync(PostgreSqlParameters parameters, CancellationToken token)
    {
        var facts = await ReadAsync<SalesLineFact>(CreateLineFactsSql(), parameters, token);
        parameters.Add("LineFacts", JsonSerializer.Serialize(facts));
    }

    /// <summary>只为源固定SQL列生成比较表达式，保留NULL三值逻辑、大小写及尾部ASCII补空格语义；B10全Unicode差异仍登记。</summary>
    private static string Different(string left, string right)
        => $"rtrim({left},' ') COLLATE public.erp_dictionary_ci <> rtrim({right},' ') COLLATE public.erp_dictionary_ci";

    /// <summary>为事实选择和分页尾键保留SQL Server uniqueidentifier字节优先级；只接收内部固定列名。</summary>
    private static string SourceUuidOrder(string column)
        => "ARRAY[" + string.Join(',', new[] { 10, 11, 12, 13, 14, 15, 8, 9, 7, 6, 5, 4, 3, 2, 1, 0 }
            .Select(offset => $"get_byte(uuid_send({column}),{offset})")) + "]";

    /// <summary>候选关系只传源临时表已有字段，日期使用无时区类型，不重新读取实体。</summary>
    private const string CandidateRelation = """
        SELECT c."Uid" AS uid,c."Version" AS version,c."DocumentStatus" AS document_status,
               c."ConfirmationStatus" AS confirmation_status,c."CustomerId" AS customer_id,
               c."OrderNo" AS order_no,c."DeliveryDate" AS delivery_date
        FROM jsonb_to_recordset(CAST(@Candidates AS jsonb)) AS c(
            "Uid" uuid,"Version" integer,"DocumentStatus" integer,"ConfirmationStatus" integer,
            "CustomerId" uuid,"OrderNo" text,"DeliveryDate" timestamp without time zone)
        """;

    /// <summary>冻结行事实的SQL关系；未知数值仍是NULL，不用默认零替换。</summary>
    private const string LineFactsRelation = """
        SELECT * FROM jsonb_to_recordset(CAST(@LineFacts AS jsonb)) AS f(
            "SalesOrderId" uuid,"SalesOrderLineId" uuid,"LineNumber" integer,"Qty" numeric,
            "UnitSnapshot" text,"EffectiveDueDate" timestamp without time zone,
            "ShippedQty" numeric,"UnshippedQty" numeric,"ReviewReason" text)
        """;

    /// <summary>局部候选头记录，负责跨命令保留源SELECT INTO得到的七项快照，不作为API模型。</summary>
    private sealed class CandidateOrder
    {
        public Guid Uid { get; set; }
        public int Version { get; set; }
        public int DocumentStatus { get; set; }
        public int ConfirmationStatus { get; set; }
        public Guid? CustomerId { get; set; }
        public string OrderNo { get; set; } = string.Empty;
        public DateTime? DeliveryDate { get; set; }
    }

    /// <summary>源销售行事实九字段的局部投影；NULL交付量与ReviewReason独立保留供三个查询入口复用。</summary>
    private sealed class SalesLineFact
    {
        public Guid SalesOrderId { get; set; }
        public Guid SalesOrderLineId { get; set; }
        public int LineNumber { get; set; }
        public decimal Qty { get; set; }
        public string? UnitSnapshot { get; set; }
        public DateTime? EffectiveDueDate { get; set; }
        public decimal? ShippedQty { get; set; }
        public decimal? UnshippedQty { get; set; }
        public string? ReviewReason { get; set; }
    }
}
