using System.Data;
using Dapper;
using Npgsql;
using NpgsqlTypes;
using SwiftExport.Contracts.DTOs.Inventory;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.Inventory.Query;
using SwiftExport.Infrastructure.Contexts;

namespace SwiftExport.Infrastructure.Repositories.Inventory.Query;

/// <summary>PG库存只读查询：保留余额当前图片、流水历史快照和两阶段分页，不写库存或接运行DI。</summary>
public sealed class PostgreSqlInventoryQueryRepository(RepositoryBaseContext context) : IInventoryQueryRepository
{
    /// <summary>先观察有效余额数量，再读取当前页；仓库/供应商不加活动或删除条件，Available等于OnHand。</summary>
    public async Task<PagedResponseBase<InventoryBalanceDto>> SearchBalancesAsync(InventoryBalanceSearchRequest request, CancellationToken cancellationToken = default)
    {
        var index = Math.Max(1, request.PageIndex);
        var size = Math.Clamp(request.PageSize <= 0 ? 50 : request.PageSize, 1, 200);
        var p = Paging(index, size);
        p.Add("SupplierId", request.SupplierId, NpgsqlDbType.Uuid);
        p.Add("WarehouseSiteId", request.WarehouseSiteId, NpgsqlDbType.Uuid);
        p.Add("NonZeroOnly", request.NonZeroOnly, NpgsqlDbType.Boolean);
        p.Add("Keyword", Like(request.Keyword), NpgsqlDbType.Text);
        const string where = """
            WHERE i.is_deleted=FALSE AND (@SupplierId IS NULL OR w.supplier_id=@SupplierId)
              AND (@WarehouseSiteId IS NULL OR i.warehouse_site_id=@WarehouseSiteId)
              AND (COALESCE(@NonZeroOnly,FALSE)=FALSE OR i.on_hand_qty<>0)
              AND (@Keyword IS NULL OR i.sku_snapshot COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE E'\\'
                OR i.product_name_snapshot COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE E'\\'
                OR w.code COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE E'\\'
                OR w.name COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE E'\\')
            """;
        // 源count重载没有令牌；保留先count后带令牌读页的边界，不新增一致性事务。
        var total = await context.Session.Connection.ExecuteScalarAsync<int>($"SELECT COUNT(1)::integer FROM public.inventory_balance i JOIN public.warehouse_site w ON w.uid=i.warehouse_site_id {where};", p, context.Session.Transaction);
        var rows = await context.Session.Connection.QueryAsync<InventoryBalanceDto>(Command($"""
            {BalanceSelect} {where}
            ORDER BY w.code COLLATE public.erp_dictionary_ci ASC NULLS FIRST,
                i.sku_snapshot COLLATE public.erp_dictionary_ci ASC NULLS FIRST,{SourceUuidOrder("i.uid")}
            LIMIT @PageSize OFFSET @Offset;
            """, p, cancellationToken));
        return Page(rows, total, index, size);
    }

    /// <summary>流水分页保留所有历史及反向/替换join，日期用原墙上输入及上界加一天，不截自然日。</summary>
    public async Task<PagedResponseBase<InventoryLedgerItemDto>> SearchLedgerAsync(InventoryLedgerSearchRequest request, CancellationToken cancellationToken = default)
    {
        var index = Math.Max(1, request.PageIndex);
        var size = Math.Clamp(request.PageSize <= 0 ? 50 : request.PageSize, 1, 200);
        var p = Paging(index, size);
        p.Add("SupplierId", request.SupplierId, NpgsqlDbType.Uuid);
        p.Add("WarehouseSiteId", request.WarehouseSiteId, NpgsqlDbType.Uuid);
        p.Add("TransactionType", request.TransactionType.HasValue ? (int)request.TransactionType.Value : null, NpgsqlDbType.Integer);
        p.Timestamp("OccurredDateFrom", request.OccurredDateFrom); p.Timestamp("OccurredDateTo", request.OccurredDateTo);
        p.Add("SourceDocumentNo", Like(request.SourceDocumentNo), NpgsqlDbType.Text); p.Add("Keyword", Like(request.Keyword), NpgsqlDbType.Text);
        const string where = """
            WHERE (@SupplierId IS NULL OR w.supplier_id=@SupplierId) AND (@WarehouseSiteId IS NULL OR t.warehouse_site_id=@WarehouseSiteId)
              AND (@TransactionType IS NULL OR t.transaction_type=@TransactionType)
              AND (@OccurredDateFrom IS NULL OR t.occurred_date>=@OccurredDateFrom)
              AND (@OccurredDateTo IS NULL OR t.occurred_date<@OccurredDateTo + INTERVAL '1 day')
              AND (@SourceDocumentNo IS NULL OR t.source_document_no_snapshot COLLATE public.erp_dictionary_ci LIKE @SourceDocumentNo ESCAPE E'\\')
              AND (@Keyword IS NULL OR t.sku_snapshot COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE E'\\'
                OR t.product_name_snapshot COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE E'\\'
                OR t.source_document_type COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE E'\\'
                OR t.warehouse_code_snapshot COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE E'\\')
            """;
        var total = await context.Session.Connection.ExecuteScalarAsync<int>($"SELECT COUNT(1)::integer FROM public.inventory_ledger_entry t JOIN public.warehouse_site w ON w.uid=t.warehouse_site_id {where};", p, context.Session.Transaction);
        var rows = await context.Session.Connection.QueryAsync<InventoryLedgerItemDto>(Command($"""
            {LedgerSelect} JOIN public.warehouse_site w ON w.uid=t.warehouse_site_id {where}
            ORDER BY t.sequence DESC NULLS LAST,{SourceUuidOrder("t.uid")} DESC LIMIT @PageSize OFFSET @Offset;
            """, p, cancellationToken));
        return Page(rows, total, index, size);
    }

    /// <summary>仅有效余额，产品缺失/删除仍保留快照并留空图片；不补空名称或Guid。</summary>
    public Task<InventoryBalanceDto?> GetBalanceDtoAsync(Guid uid, CancellationToken cancellationToken = default)
    {
        var p = new InventoryParameters(); p.Add("Uid", uid, NpgsqlDbType.Uuid);
        return context.Session.Connection.QuerySingleOrDefaultAsync<InventoryBalanceDto>(Command($"{BalanceSelect} WHERE i.uid=@Uid AND i.is_deleted=FALSE;", p, cancellationToken));
    }

    /// <summary>按余额仓库/产品键读取全部历史，即使余额软删仍可Trace；此入口页大小直接clamp而不回落50。</summary>
    public async Task<PagedResponseBase<InventoryLedgerItemDto>> GetLedgerByBalanceAsync(Guid balanceId, int pageIndex, int pageSize, CancellationToken cancellationToken = default)
    {
        var index = Math.Max(1, pageIndex); var size = Math.Clamp(pageSize, 1, 200);
        var p = Paging(index, size); p.Add("BalanceId", balanceId, NpgsqlDbType.Uuid);
        const string join = "JOIN public.inventory_balance i ON i.warehouse_site_id=t.warehouse_site_id AND i.product_id=t.product_id JOIN public.warehouse_site w ON w.uid=t.warehouse_site_id WHERE i.uid=@BalanceId";
        var total = await context.Session.Connection.ExecuteScalarAsync<int>($"SELECT COUNT(1)::integer FROM public.inventory_ledger_entry t {join};", p, context.Session.Transaction);
        var rows = await context.Session.Connection.QueryAsync<InventoryLedgerItemDto>(Command($"{LedgerSelect} {join} ORDER BY t.sequence DESC NULLS LAST,{SourceUuidOrder("t.uid")} DESC LIMIT @PageSize OFFSET @Offset;", p, cancellationToken));
        return Page(rows, total, index, size);
    }

    /// <summary>有效余额对应全部流水SUM/MAX同时对账；没有流水默认0，没有余额返回NULL，不自动修复。</summary>
    public Task<InventoryReconciliationDto?> GetReconciliationAsync(Guid balanceId, CancellationToken cancellationToken = default)
    {
        var p = new InventoryParameters(); p.Add("BalanceId", balanceId, NpgsqlDbType.Uuid);
        return context.Session.Connection.QuerySingleOrDefaultAsync<InventoryReconciliationDto>(Command("""
            SELECT i.uid AS "BalanceId",i.on_hand_qty AS "BalanceQty",COALESCE(SUM(t.quantity_delta),0) AS "LedgerNetQty",
                i.last_ledger_sequence AS "BalanceLastLedgerSequence",COALESCE(MAX(t.sequence),0) AS "LedgerLastSequence",
                CASE WHEN i.on_hand_qty=COALESCE(SUM(t.quantity_delta),0) AND i.last_ledger_sequence=COALESCE(MAX(t.sequence),0) THEN TRUE ELSE FALSE END AS "IsReconciled"
            FROM public.inventory_balance i LEFT JOIN public.inventory_ledger_entry t ON t.warehouse_site_id=i.warehouse_site_id AND t.product_id=i.product_id
            WHERE i.uid=@BalanceId AND i.is_deleted=FALSE GROUP BY i.uid,i.on_hand_qty,i.last_ledger_sequence;
            """, p, cancellationToken));
    }

    /// <summary>借父事务和行阶段令牌，不接管会话生命周期。</summary>
    private CommandDefinition Command(string sql, InventoryParameters p, CancellationToken token) => new(sql, p, context.Session.Transaction, cancellationToken: token);
    /// <summary>保留源Trim及反斜线/%/下划线/左方括号转义；不套用其他页面转义。</summary>
    private static string? Like(string? value)
    {
        var text = value?.Trim(); return string.IsNullOrEmpty(text) ? null : "%" + text.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_").Replace("[", "\\[") + "%";
    }
    /// <summary>组装原int分页参数，不新增跨层校验或溢出规则。</summary>
    private static InventoryParameters Paging(int index, int size)
    { var p = new InventoryParameters(); p.Add("Offset", (index - 1) * size, NpgsqlDbType.Integer); p.Add("PageSize", size, NpgsqlDbType.Integer); return p; }
    /// <summary>只物化页行，不排序、截页或重算历史。</summary>
    private static PagedResponseBase<T> Page<T>(IEnumerable<T> rows, int total, int index, int size) => new() { Items=rows.ToList(),TotalCount=total,PageIndex=index,PageSize=size };
    /// <summary>按SQL Server uniqueidentifier字节优先级排序，区分PG原生uuid及.NET Guid。</summary>
    private static string SourceUuidOrder(string column) => "ARRAY[" + string.Join(",",new[]{10,11,12,13,14,15,8,9,7,6,5,4,3,2,1,0}.Select(offset=>$"get_byte(uuid_send({column}),{offset})")) + "]";

    /// <summary>局部显式绑定nullable标量和无时区时间；count与页分别克隆，不改公共参数。</summary>
    private sealed class InventoryParameters : SqlMapper.IDynamicParameters
    {
        private readonly List<NpgsqlParameter> values=[];
        /// <summary>空值仍保留数据库类型，不让驱动猜测UUID/布尔/整数。</summary>
        public void Add(string name,object? value,NpgsqlDbType type)=>values.Add(new(name,type){Value=value??DBNull.Value});
        /// <summary>墙上时间剥离Kind，不换算UTC，不截Date。</summary>
        public void Timestamp(string name,DateTime? value)=>Add(name,value.HasValue?DateTime.SpecifyKind(value.Value,DateTimeKind.Unspecified):null,NpgsqlDbType.Timestamp);
        /// <summary>将克隆交给当前命令，不关闭连接/事务。</summary>
        public void AddParameters(IDbCommand command,SqlMapper.Identity identity){foreach(var value in values)command.Parameters.Add(value.Clone());}
    }

    // 余额名称和单位来自过账快照；只有图片取当前有效产品，供应商资料不加过滤/回落。
    private const string BalanceSelect = """
        SELECT i.uid AS "Uid",i.version AS "Version",i.warehouse_site_id AS "WarehouseSiteId",w.code AS "WarehouseCode",w.name AS "WarehouseName",w.supplier_id AS "SupplierId",
            s.supplier_code AS "SupplierCode",s.supplier_name AS "SupplierName",i.product_id AS "ProductId",i.sku_snapshot AS "Sku",i.product_name_snapshot AS "ProductName",
            i.unit_snapshot AS "Unit",p.main_image AS "ImagePath",i.on_hand_qty AS "OnHandQty",i.on_hand_qty AS "AvailableQty",i.last_ledger_sequence AS "LastLedgerSequence",i.updated_date AS "UpdatedDate"
        FROM public.inventory_balance i JOIN public.warehouse_site w ON w.uid=i.warehouse_site_id LEFT JOIN public.supplier s ON s.uid=w.supplier_id
        LEFT JOIN public.product p ON p.uid=i.product_id AND p.is_deleted=FALSE
        """;

    // 流水字段坚持历史快照，反向与替换分别LEFT JOIN，不清理、排删或额外去重。
    private const string LedgerSelect = """
        SELECT t.uid AS "Uid",t.sequence AS "Sequence",t.warehouse_site_id AS "WarehouseSiteId",t.warehouse_code_snapshot AS "WarehouseCode",t.warehouse_name_snapshot AS "WarehouseName",
            t.product_id AS "ProductId",t.sku_snapshot AS "Sku",t.product_name_snapshot AS "ProductName",t.unit_snapshot AS "Unit",t.transaction_type AS "TransactionType",
            t.quantity_delta AS "QuantityDelta",t.balance_after AS "BalanceAfter",t.source_document_type AS "SourceDocumentType",t.source_document_id AS "SourceDocumentId",t.source_line_id AS "SourceLineId",
            t.source_document_no_snapshot AS "SourceDocumentNo",t.reversal_of_ledger_id AS "ReversalOfLedgerId",rv.uid AS "ReversedByLedgerId",t.replacement_of_ledger_id AS "ReplacementOfLedgerId",
            rp.uid AS "ReplacedByLedgerId",t.correction_group_id AS "CorrectionGroupId",t.reason AS "Reason",t.occurred_date AS "OccurredDate",t.created_by AS "CreatedBy"
        FROM public.inventory_ledger_entry t LEFT JOIN public.inventory_ledger_entry rv ON rv.reversal_of_ledger_id=t.uid LEFT JOIN public.inventory_ledger_entry rp ON rp.replacement_of_ledger_id=t.uid
        """;
}
