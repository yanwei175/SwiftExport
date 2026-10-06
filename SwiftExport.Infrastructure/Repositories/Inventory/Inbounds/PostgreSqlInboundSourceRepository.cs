using Dapper;
using System.Data;
using SwiftExport.Contracts.DTOs.Inventory.Inbounds.Posting;
using SwiftExport.Contracts.DTOs.Inventory.Inbounds.Sources;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Common;
using SwiftExport.Core.Interfaces.Inventory.Inbounds;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.Inventory.Inbounds;

/// <summary>入库采购来源完整 PostgreSQL 查询与已有头行锁；复用历史门禁，不创建事务、预占额度或替代联合并发协议。</summary>
public sealed class PostgreSqlInboundSourceRepository(RepositoryBaseContext context) : IInboundSourceRepository
{
    /// <summary>沿用原可空会话事务及取消令牌，异常交给调用方。</summary>
    private CommandDefinition Command(string sql, object? args, CancellationToken token) =>
        new(sql, args, context.Session.Transaction, cancellationToken: token);

    /// <summary>先检查反向及未知来源历史，再物化同一候选集合用于计数和数据库分页；保留原资格和草稿提示，不加锁。</summary>
    public async Task<PagedResponseBase<InboundSourceCandidateDto>> SearchSourcesAsync(InboundSourceSearchRequest request, CancellationToken token)
    {
        var parameters = PostingParameters();
        parameters.Add("CustomerSupplied", request.PurchaseCategory == InboundPurchaseCategoryEnum.CustomerSupplied);
        parameters.Add("SupplierId", request.SupplierId);
        parameters.Add("CurrentInboundDocumentId", request.CurrentInboundDocumentId);
        parameters.Add("Keyword", Like(request.Keyword));
        parameters.Add("Skip", (long)(request.PageIndex - 1) * request.PageSize);
        parameters.Add("Take", request.PageSize);
        await PostgreSqlInboundReceiptHistoryGuard.ValidateGlobalAsync(context.Session, token);
        if (await HasUnprovenReceiptAsync(token)) throw new UnverifiedReceiptHistoryException();
        var sql = "WITH candidates AS MATERIALIZED (" + SourceProjection + "\n" + """
            AND h.confirmation_status=@Confirmed AND h.lifecycle_status=@Active
            AND rtrim(l.material_type,' ') COLLATE public.erp_dictionary_ci=ANY(
                SELECT rtrim(value,' ') FROM unnest(@KnownTypes) input(value))
            AND ((@CustomerSupplied AND rtrim(l.material_type,' ') COLLATE public.erp_dictionary_ci=@CustomerType)
                OR (NOT @CustomerSupplied AND rtrim(l.material_type,' ') COLLATE public.erp_dictionary_ci<>@CustomerType))
            AND (CAST(@SupplierId AS uuid) IS NULL OR h.supplier_id=@SupplierId)
            AND l.quantity>COALESCE(received.qty,0)
            AND (CAST(@Keyword AS text) IS NULL OR h.order_no COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~'
                OR h.bo_no COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~'
                OR l.sku COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~'
                OR l.link_sku COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~')
            ), page AS MATERIALIZED (
            SELECT * FROM candidates
            """ + $" ORDER BY {CandidateOrder()} LIMIT CASE WHEN @Take<=0 THEN -1 ELSE @Take END OFFSET @Skip)" + $"""
            SELECT total."TotalCount",page.*
            FROM (SELECT COUNT(*)::int AS "TotalCount" FROM candidates) total LEFT JOIN page ON TRUE
            ORDER BY {CandidateOrder("page.")};
            """;
        // 空页仍返回计数行；Dapper 分段的首列 DocumentType 为 NULL 时候选对象为空，不向业务返回占位 DTO。
        var rows = (await context.Session.Connection.QueryAsync<SourceCount, InboundSourceCandidateDto, (int Total, InboundSourceCandidateDto? Item)>(
            Command(sql, parameters, token), (count, item) => (count.TotalCount, item), splitOn: "DocumentType")).ToList();
        return new PagedResponseBase<InboundSourceCandidateDto>
        {
            TotalCount = rows[0].Total,
            Items = rows.Where(row => row.Item is not null).Select(row => row.Item!).ToList(),
            PageIndex = request.PageIndex,
            PageSize = request.PageSize
        };
    }

    /// <summary>读取指定采购行身份和快照，不按采购状态、数量或版本拒绝草稿；空集合仍先检查全局历史。</summary>
    public async Task<IReadOnlyList<InboundSourceCandidateDto>> GetSourcesAsync(IReadOnlyList<Guid> ids, CancellationToken token)
    {
        var parameters = new PostgreSqlParameters();
        parameters.AddIds("Ids", ids);
        parameters.Add("CurrentInboundDocumentId", null);
        await PostgreSqlInboundReceiptHistoryGuard.ValidateGlobalAsync(context.Session, token);
        return (await context.Session.Connection.QueryAsync<InboundSourceCandidateDto>(
            Command(SourceProjection + " AND l.uid=ANY(@Ids)", parameters, token))).ToList();
    }

    /// <summary>空集合沿用零查询短路；非空先检查全局历史，再返回包含失效和可空产品事实的完整过账投影。</summary>
    public async Task<IReadOnlyList<InboundPurchasePostingSourceDto>> ReadPostingSourcesAsync(IReadOnlyList<Guid> ids, CancellationToken token)
    {
        if (ids.Count == 0) return [];
        var parameters = PostingParameters();
        parameters.AddIds("Ids", ids);
        await PostgreSqlInboundReceiptHistoryGuard.ValidateGlobalAsync(context.Session, token);
        return (await context.Session.Connection.QueryAsync<InboundPurchasePostingSourceDto>(Command("""
            SELECT l.uid AS "SourceLineId",l.purchase_order_id AS "PurchaseOrderId",l.product_id AS "ProductId",
                h.order_no AS "SourceNo",l.unit_snapshot AS "Unit",p.unit AS "CurrentUnit",l.quantity AS "OriginalQty",
                CASE WHEN h.is_deleted=FALSE AND l.is_deleted=FALSE AND p.is_deleted=FALSE
                    AND h.confirmation_status=@Confirmed AND h.lifecycle_status=@Active
                    AND rtrim(l.material_type,' ') COLLATE public.erp_dictionary_ci=ANY(
                        SELECT rtrim(value,' ') FROM unnest(@KnownTypes) input(value))
                    THEN TRUE ELSE FALSE END AS "IsReceivable",
                CASE WHEN rtrim(l.material_type,' ') COLLATE public.erp_dictionary_ci=@CustomerType THEN TRUE ELSE FALSE END AS "IsCustomerSupplied",
                COALESCE((SELECT SUM(CASE WHEN ih.posting_status=1 THEN il.quantity ELSE 0 END)
                    FROM public.inbound_document_line il JOIN public.inbound_document ih ON ih.uid=il.inbound_document_id
                    WHERE il.source_line_id=l.uid AND ih.document_type=10 AND ih.posting_status IN(1,8)
                        AND il.is_deleted=FALSE AND ih.is_deleted=FALSE),0) AS "PostedNetQty"
            FROM public.purchase_order_line l JOIN public.purchase_order_head h ON h.uid=l.purchase_order_id
            LEFT JOIN public.product p ON p.uid=l.product_id WHERE l.uid=ANY(@Ids);
            """, parameters, token))).ToList();
    }

    /// <summary>按原 .NET Guid 顺序先锁发现的采购头再锁去重采购行，归属变到未锁头时抛原并发异常；不伪造缺失范围锁。</summary>
    public async Task LockPurchaseSourcesAsync(IReadOnlyList<Guid> ids, CancellationToken token)
    {
        if (ids.Count == 0) return;
        var parameters = new PostgreSqlParameters();
        parameters.AddIds("Ids", ids);
        var owners = (await context.Session.Connection.QueryAsync<Guid>(Command(
            "SELECT DISTINCT purchase_order_id FROM public.purchase_order_line WHERE uid=ANY(@Ids)", parameters, token))).OrderBy(id => id).ToArray();
        foreach (var id in owners)
            await context.Session.Connection.QueryAsync<Guid>(Command("SELECT uid FROM public.purchase_order_head WHERE uid=@Id FOR UPDATE", new { Id = id }, token));
        foreach (var id in ids.Distinct().OrderBy(value => value))
        {
            var owner = await context.Session.Connection.QuerySingleOrDefaultAsync<Guid?>(Command(
                "SELECT purchase_order_id FROM public.purchase_order_line WHERE uid=@Id FOR UPDATE", new { Id = id }, token));
            if (owner.HasValue && !owners.Contains(owner.Value)) throw new DBConcurrencyException("采购来源归属已变化，请重新预览。");
        }
    }

    /// <summary>仅按原全局正式历史范围检查未知来源；不扩大为通用反向门禁，不把未知当作零收货。</summary>
    public Task<bool> HasUnprovenReceiptAsync(CancellationToken token) =>
        context.Session.Connection.ExecuteScalarAsync<bool>(Command("""
            SELECT EXISTS(SELECT 1 FROM public.inbound_document_line il
                JOIN public.inbound_document ih ON ih.uid=il.inbound_document_id
                LEFT JOIN public.purchase_order_line pl ON pl.uid=il.source_line_id
                WHERE ih.is_deleted=FALSE AND il.is_deleted=FALSE AND ih.posting_status IN(1,8)
                    AND (ih.document_type IN(11,12) OR (ih.document_type=10 AND pl.uid IS NULL)));
            """, null, token));

    // 保存快照仅检查现有头行/产品及非空单位；正式已过账贡献数量，反向为零，其他草稿不扣额度。
    private const string SourceProjection = """
        SELECT 10 AS "DocumentType",l.uid AS "SourceLineId",l.version AS "SourceVersion",l.purchase_order_id AS "PurchaseOrderId",
               l.product_id AS "ProductId",h.order_no AS "SourceNo",l.line_number AS "LineNumber",h.bo_no AS "BoNo",
               l.sku AS "Sku",l.link_sku AS "LinkSku",l.description_zh AS "Description",product.main_image AS "ImagePath",
               l.unit_snapshot AS "Unit",h.supplier_name AS "SupplierName",default_warehouse.name AS "DefaultWarehouseName",
               l.quantity AS "OriginalQty",l.quantity-COALESCE(received.qty,0) AS "RemainingReceivableQty",
               drafts.other_draft_count AS "OtherDraftCount",drafts.other_draft_quantity AS "OtherDraftQuantity"
        FROM public.purchase_order_line l JOIN public.purchase_order_head h ON h.uid=l.purchase_order_id
        JOIN public.product product ON product.uid=l.product_id
        LEFT JOIN public.warehouse_site default_warehouse ON default_warehouse.uid=product.default_warehouse_site_id AND default_warehouse.is_deleted=FALSE
        LEFT JOIN LATERAL (
            SELECT SUM(CASE WHEN rh.posting_status=1 THEN rl.quantity ELSE 0 END) AS qty
            FROM public.inbound_document_line rl JOIN public.inbound_document rh ON rh.uid=rl.inbound_document_id
            WHERE rl.source_line_id=l.uid AND rh.document_type=10 AND rh.posting_status IN(1,8)
                AND rl.is_deleted=FALSE AND rh.is_deleted=FALSE
        ) received ON TRUE
        LEFT JOIN LATERAL (
            SELECT COUNT(DISTINCT dh.uid)::int AS other_draft_count,COALESCE(SUM(dl.quantity),0) AS other_draft_quantity
            FROM public.inbound_document_line dl JOIN public.inbound_document dh ON dh.uid=dl.inbound_document_id
            WHERE dl.source_line_id=l.uid AND dh.document_type=10 AND dh.posting_status=0 AND dh.lifecycle_status=0
                AND dl.is_deleted=FALSE AND dh.is_deleted=FALSE
                AND (CAST(@CurrentInboundDocumentId AS uuid) IS NULL OR dh.uid<>@CurrentInboundDocumentId)
        ) drafts ON TRUE
        WHERE l.is_deleted=FALSE AND h.is_deleted=FALSE AND product.is_deleted=FALSE AND l.product_id IS NOT NULL
            AND NULLIF(btrim(l.unit_snapshot,' '),'') IS NOT NULL
        """;

    /// <summary>绑定原采购确认、生命周期及物料类型条件；文本数组不改值或去重。</summary>
    private static PostgreSqlParameters PostingParameters()
    {
        var parameters = new PostgreSqlParameters();
        parameters.Add("Confirmed", (int)ConfirmationStatusEnum.已确认);
        parameters.Add("Active", (int)PurchaseOrderLifecycleStatusEnum.Active);
        parameters.AddTexts("KnownTypes", MaterialTypesEnum.MaterialTypes);
        parameters.Add("CustomerType", MaterialTypesEnum.客供件);
        return parameters;
    }

    /// <summary>按源字面关键字规则转义，不把空格或问号改成通配符。</summary>
    private static string? Like(string? value) => string.IsNullOrWhiteSpace(value) ? null :
        "%" + value.Trim().Replace("~", "~~").Replace("%", "~%").Replace("_", "~_").Replace("[", "~[") + "%";

    /// <summary>候选分页保留源字符比较、NULL 方向和 SqlGuid 尾序；不在内存排序业务行。</summary>
    private static string CandidateOrder(string prefix = "") =>
        $"rtrim({prefix}\"SourceNo\",' ') COLLATE public.erp_dictionary_ci DESC NULLS LAST,{prefix}\"LineNumber\" ASC NULLS FIRST,{UuidOrder(prefix + "\"SourceLineId\"")} ASC NULLS FIRST";

    /// <summary>复用已冻结 SQL Server uniqueidentifier 的字节优先级。</summary>
    private static string UuidOrder(string column) => "ARRAY[" + string.Join(',',
        new[] { 10, 11, 12, 13, 14, 15, 8, 9, 7, 6, 5, 4, 3, 2, 1, 0 }.Select(index => $"get_byte(uuid_send({column}),{index})")) + "]";

    /// <summary>仅承接同次物化候选的整数计数，不作为业务 DTO 返回。</summary>
    private sealed class SourceCount
    {
        /// <summary>同一物化候选集合的总行数。</summary>
        public int TotalCount { get; set; }
    }
}
