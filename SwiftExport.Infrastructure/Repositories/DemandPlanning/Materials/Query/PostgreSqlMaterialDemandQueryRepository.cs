using Dapper;
using SwiftExport.Core.Interfaces.DemandPlanning.Materials.Query;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.DemandPlanning;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Entities.Procurement;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;
using System.Text.Json;

namespace SwiftExport.Infrastructure.Repositories.DemandPlanning.Materials.Query;

/// <summary>提供 PostgreSQL 物料工作台、来源导航和分阶段 Trace；借用调用者会话，事务由应用用例持有。</summary>
public sealed partial class PostgreSqlMaterialDemandQueryRepository(RepositoryBaseContext context) : IMaterialDemandQueryRepository
{

    /// <summary>以单 SQL 物化工作集并统一快捷过滤、计数与页面；空页仍返回总量，来源模式完整取数，事务与取消沿用调用方。</summary>
    public async Task<PagedResponseBase<MaterialDemandManagerItemDto>> SearchManagerAsync(
        MaterialDemandManagerSearchRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var pageIndex = Math.Max(1, request.PageIndex);
        var pageSize = Math.Clamp(request.PageSize <= 0 ? 20 : request.PageSize, 1, 200);
        var paged = !request.SourceUid.HasValue;
        var parameters = new PostgreSqlParameters();
        parameters.Add("PurchaseTargetType", "PO");
        // 一次取得 China Standard Time 业务日，不借用数据库服务器时区或重复读取午夜时钟。
        parameters.AddTimestamp("BusinessDate", TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow,
            TimeZoneInfo.FindSystemTimeZoneById("China Standard Time")).Date);
        if (paged) { parameters.Add("Offset", (pageIndex - 1) * pageSize); parameters.Add("PageSize", pageSize); }
        var where = CreateWorkspaceWhere(request, parameters);
        var quickWhere = request.QuickView switch
        {
            "Unassigned" => "\"ProcurementStatus\"='未承接'",
            "Partial" => "\"ProcurementStatus\"='部分承接'",
            "Assigned" => "\"ProcurementStatus\"='已承接'",
            "Overdue" => "\"DueHint\"='已逾期'",
            "DueSoon" => "\"DueHint\"='临近交期'",
            "Review" => "\"ReviewReason\"<>''",
            _ => "TRUE"
        };
        var pageClause = paged ? "LIMIT @PageSize OFFSET @Offset" : string.Empty;
        var sql = $"""
            WITH workspace AS MATERIALIZED (
                SELECT
                    nd.uid AS "Uid",
                    nd.version AS "Version",
                    nd.lifecycle_status AS "LifecycleStatus",
                    nd.gross_demand_id AS "GrossDemandId",
                    nd.decomposition_revision AS "DecompositionRevision",
                    nd.line_number AS "LineNumber",
                    gd.customer_id AS "CustomerId",
                    customer.customer_code AS "CustomerCode",
                    customer.customer_name AS "CustomerName",
                    gd.source_type AS "SourceType",
                    gd.source_uid AS "SourceUid",
                    gd.source_no AS "SourceNo",
                    gd.source_line_number AS "SourceLineNumber",
                    gd.source_line_id AS "SourceLineId",
                    nd.root_net_demand_id AS "RootNetDemandId",
                    root.sku_snapshot AS "RootSku",
                    (nd.delivery_mode=1) AS "IsCustomerSupplied",
                    nd.delivery_target_net_demand_id AS "DeliveryTargetNetDemandId",
                    nd.product_id AS "ProductId",
                    (nd.product_id IS NULL) AS "IsNonStandard",
                    nd.sku_snapshot AS "Sku",
                    COALESCE(pn.product_code,gd.link_sku_snapshot) AS "LinkSku",
                    nd.customer_sku_snapshot AS "CustomerSku",
                    nd.description_snapshot AS "Description",
                    nd.color_snapshot AS "Color",
                    nd.specification_snapshot AS "Specification",
                    CASE WHEN nd.delivery_mode=1
                         THEN COALESCE(p.main_image,'')
                         ELSE COALESCE(NULLIF(btrim(gd.image_path_snapshot, ' '),''),p.main_image,'')
                    END AS "ImagePath",
                    nd.material_type_snapshot AS "MaterialType",
                    unitFacts.unit AS "Unit",
                    nd.supply_type_snapshot AS "SupplyType",
                    nd.suggested_purchase_supplier_id AS "SuggestedPurchaseSupplierId",
                    suggestedSupplier.supplier_code AS "SuggestedPurchaseSupplierCode",
                    suggestedSupplier.supplier_name AS "SuggestedPurchaseSupplierName",
                    CASE WHEN nd.delivery_mode=1 THEN deliveryTarget.suggested_purchase_supplier_id ELSE NULL::uuid END AS "SuggestedDeliverySupplierId",
                    CASE WHEN nd.delivery_mode=1 THEN deliverySupplier.supplier_code ELSE NULL::text END AS "SuggestedDeliverySupplierCode",
                    CASE WHEN nd.delivery_mode=1 THEN deliverySupplier.supplier_name ELSE NULL::text END AS "SuggestedDeliverySupplierName",
                    CASE WHEN nd.delivery_mode<>1 THEN NULL::text
                         WHEN deliveryTarget.uid IS NULL THEN '送达目标关系无效'
                         WHEN deliveryTarget.suggested_purchase_supplier_id IS NULL THEN '父件尚未设置推荐采购供应商'
                         WHEN deliverySupplier.uid IS NULL THEN '父件推荐采购供应商已失效'
                         ELSE NULL END AS "SuggestedDeliverySupplierHint",
                    nd.gross_qty AS "GrossQty",
                    nd.net_qty AS "NetQty",
                    nd.required_date AS "DemandRequiredDate",
                    CASE WHEN nd.lifecycle_status=0 AND assessment.review_reason=''
                         THEN COALESCE(purchaseFacts.associated_purchase_qty,0) ELSE NULL::numeric END AS "AssociatedPurchaseQty",
                    purchaseNumbers.purchase_order_nos AS "PurchaseOrderNos",
                    purchaseNumbers.actual_purchase_suppliers AS "ActualPurchaseSuppliers",
                    CASE WHEN nd.lifecycle_status<>0 THEN '历史'
                         WHEN assessment.review_reason<>'' THEN '需核实'
                         WHEN COALESCE(purchaseFacts.associated_purchase_qty,0)=0 THEN '未承接'
                         WHEN purchaseFacts.associated_purchase_qty<nd.net_qty THEN '部分承接'
                         ELSE '已承接' END AS "ProcurementStatus",
                    CASE WHEN nd.lifecycle_status<>0 OR assessment.review_reason<>''
                               OR nd.required_date IS NULL OR COALESCE(purchaseFacts.associated_purchase_qty,0)>=nd.net_qty
                         THEN ''
                         WHEN nd.required_date::date<@BusinessDate THEN '已逾期'
                         WHEN nd.required_date::date<=(CAST(@BusinessDate AS date) + 7) THEN '临近交期'
                         ELSE '' END AS "DueHint",
                    assessment.review_reason AS "ReviewReason",
                    NULL::text AS "SuggestedSupplierSku",
                    NULL::numeric AS "SuggestedUnitPrice",
                    NULL::numeric AS "SuggestedTaxPrice"
                FROM "public"."net_demand" nd
                INNER JOIN "public"."gross_demand" gd ON gd.uid=nd.gross_demand_id
                LEFT JOIN "public"."customer" customer ON customer.uid=gd.customer_id AND COALESCE(customer.is_deleted,FALSE)=FALSE
                LEFT JOIN "public"."net_demand" root ON root.uid=nd.root_net_demand_id
                LEFT JOIN "public"."product" p ON p.uid=nd.product_id AND COALESCE(p.is_deleted,FALSE)=FALSE
                LEFT JOIN public.product_neutral pn ON pn.uid=p.product_neutral_id AND pn.is_deleted=FALSE
                LEFT JOIN "public"."supplier" suggestedSupplier
                  ON suggestedSupplier.uid=nd.suggested_purchase_supplier_id AND COALESCE(suggestedSupplier.is_deleted,FALSE)=FALSE
                LEFT JOIN "public"."net_demand" deliveryTarget
                  ON deliveryTarget.uid=nd.delivery_target_net_demand_id
                 AND deliveryTarget.uid=nd.parent_net_demand_id
                 AND deliveryTarget.gross_demand_id=nd.gross_demand_id
                 AND deliveryTarget.root_net_demand_id=nd.root_net_demand_id
                 AND deliveryTarget.decomposition_revision=nd.decomposition_revision
                 AND deliveryTarget.lifecycle_status=0 AND deliveryTarget.is_deleted=FALSE
                LEFT JOIN "public"."supplier" deliverySupplier
                  ON deliverySupplier.uid=deliveryTarget.suggested_purchase_supplier_id AND COALESCE(deliverySupplier.is_deleted,FALSE)=FALSE
                LEFT JOIN LATERAL (SELECT NULLIF(btrim(nd.unit_snapshot, ' '),'') AS unit) unitFacts ON TRUE
                -- 保留源工作台：先汇总关联量，再独立判断目标可信度，避免把失效目标漏成未承接。
                LEFT JOIN LATERAL (
                    SELECT SUM(link.allocated_qty-link.released_qty) AS associated_purchase_qty
                    FROM "public"."demand_execution_link" link
                    LEFT JOIN "public"."purchase_order_head" purchaseHead
                      ON purchaseHead.uid=link.target_head_id
                     AND purchaseHead.lifecycle_status=0 AND COALESCE(purchaseHead.is_deleted,FALSE)=FALSE
                    LEFT JOIN "public"."purchase_order_line" purchaseLine
                      ON purchaseLine.uid=link.target_line_id
                     AND purchaseLine.purchase_order_id=purchaseHead.uid AND COALESCE(purchaseLine.is_deleted,FALSE)=FALSE
                    WHERE link.net_demand_id=nd.uid
                      AND rtrim(link.target_type, ' ') COLLATE public.erp_dictionary_ci=rtrim(@PurchaseTargetType, ' ') COLLATE public.erp_dictionary_ci AND link.link_status=0 AND link.is_deleted=FALSE
                      AND link.allocated_qty>link.released_qty
                ) purchaseFacts ON TRUE
                LEFT JOIN LATERAL (
                    SELECT COUNT(*) AS invalid_count
                    FROM "public"."demand_execution_link" suspectLink
                    LEFT JOIN "public"."purchase_order_head" suspectHead ON suspectHead.uid=suspectLink.target_head_id
                        AND suspectHead.lifecycle_status=0 AND suspectHead.is_deleted=FALSE
                    LEFT JOIN "public"."purchase_order_line" suspectLine ON suspectLine.uid=suspectLink.target_line_id
                        AND suspectLine.purchase_order_id=suspectLink.target_head_id AND suspectLine.is_deleted=FALSE
                    WHERE suspectLink.net_demand_id=nd.uid AND rtrim(suspectLink.target_type, ' ') COLLATE public.erp_dictionary_ci='PO' COLLATE public.erp_dictionary_ci
                      AND suspectLink.link_status=0 AND suspectLink.is_deleted=FALSE
                      AND suspectLink.allocated_qty>suspectLink.released_qty
                      AND (suspectHead.uid IS NULL OR suspectLine.uid IS NULL OR suspectHead.customer_id IS NULL
                           OR suspectHead.customer_id<>gd.customer_id OR unitFacts.unit IS NULL
                           OR NULLIF(btrim(suspectLine.unit_snapshot, ' '),'') IS NULL
                           OR btrim(suspectLine.unit_snapshot, ' ') COLLATE public.erp_dictionary_ci<>unitFacts.unit COLLATE public.erp_dictionary_ci)
                ) purchaseIssues ON TRUE
                LEFT JOIN LATERAL (
                    SELECT string_agg(numbers.order_no,',') AS purchase_order_nos,
                           string_agg(numbers.supplier_name,',') AS actual_purchase_suppliers
                    FROM
                    (
                        SELECT DISTINCT ON (rtrim(purchaseHead.order_no, ' ') COLLATE public.erp_dictionary_ci, rtrim(supplier.supplier_name, ' ') COLLATE public.erp_dictionary_ci) purchaseHead.order_no,supplier.supplier_name
                        FROM "public"."demand_execution_link" link
                        INNER JOIN "public"."purchase_order_head" purchaseHead
                          ON purchaseHead.uid=link.target_head_id
                         AND purchaseHead.lifecycle_status=0 AND COALESCE(purchaseHead.is_deleted,FALSE)=FALSE
                        INNER JOIN "public"."purchase_order_line" purchaseLine
                          ON purchaseLine.uid=link.target_line_id
                         AND purchaseLine.purchase_order_id=purchaseHead.uid AND COALESCE(purchaseLine.is_deleted,FALSE)=FALSE
                        LEFT JOIN "public"."supplier" supplier ON supplier.uid=purchaseHead.supplier_id
                        WHERE link.net_demand_id=nd.uid
                          AND rtrim(link.target_type, ' ') COLLATE public.erp_dictionary_ci=rtrim(@PurchaseTargetType, ' ') COLLATE public.erp_dictionary_ci AND link.link_status=0 AND link.is_deleted=FALSE
                          AND link.allocated_qty>link.released_qty
                    ) numbers
                ) purchaseNumbers ON TRUE
                -- 历史数量保持未知；有效行按客户、来源、单位、目标、超量顺序返回首要核实原因。
                LEFT JOIN LATERAL (SELECT CASE
                    WHEN nd.lifecycle_status<>0 THEN ''
                    WHEN gd.customer_id IS NULL THEN '来源客户缺失'
                    WHEN nd.source_demand_version_snapshot IS NULL OR nd.source_demand_version_snapshot<>gd.version
                      OR NULLIF(rtrim(gd.demand_basis_fingerprint, ' '),'') IS NULL
                      OR nd.source_demand_basis_fingerprint_snapshot IS NULL
                      OR rtrim(nd.source_demand_basis_fingerprint_snapshot, ' ') COLLATE public.erp_dictionary_ci<>rtrim(gd.demand_basis_fingerprint, ' ') COLLATE public.erp_dictionary_ci
                      OR (rtrim(gd.source_type, ' ') COLLATE public.erp_dictionary_ci<>'MD' COLLATE public.erp_dictionary_ci AND (gd.source_version IS NULL OR nd.source_version_snapshot IS NULL
                          OR nd.source_version_snapshot<>gd.source_version))
                      OR (rtrim(gd.source_type, ' ') COLLATE public.erp_dictionary_ci='MD' COLLATE public.erp_dictionary_ci AND nd.source_version_snapshot IS NOT NULL)
                      THEN '来源版本或需求指纹需核实'
                    WHEN unitFacts.unit IS NULL THEN '物料单位需核实'
                    WHEN nd.net_qty<=0 THEN '物料需求数量需核实'
                    WHEN purchaseIssues.invalid_count>0 THEN '采购目标、客户或单位需核实'
                    WHEN COALESCE(purchaseFacts.associated_purchase_qty,0)>nd.net_qty THEN '采购承接超量需核实'
                    ELSE '' END AS review_reason) assessment ON TRUE
                WHERE {string.Join(" AND ", where)}
            ), filtered AS (SELECT {WorkspaceColumns} FROM workspace WHERE {quickWhere}), page AS (
                SELECT {WorkspaceColumns} FROM filtered
                ORDER BY "SourceNo" COLLATE public.erp_dictionary_ci NULLS FIRST,
                         "SourceLineNumber" NULLS FIRST, "LineNumber", "Uid"
                {pageClause}
            )
            SELECT (SELECT COUNT(*)::integer FROM filtered) AS "TotalCount",
                   COALESCE((SELECT jsonb_agg(to_jsonb(page) ORDER BY
                       page."SourceNo" COLLATE public.erp_dictionary_ci NULLS FIRST,
                       page."SourceLineNumber" NULLS FIRST, page."LineNumber", page."Uid") FROM page), '[]'::jsonb)::text AS "ItemsJson";
            """;
        var result = await context.Session.Connection.QuerySingleAsync<WorkspaceQueryRow>(Command(sql, parameters, cancellationToken));
        var items = JsonSerializer.Deserialize<List<MaterialDemandManagerItemDto>>(result.ItemsJson) ?? [];
        return new()
        {
            Items = items,
            TotalCount = result.TotalCount,
            PageIndex = paged ? pageIndex : 1,
            PageSize = paged ? pageSize : Math.Max(1, result.TotalCount)
        };
    }

    /// <summary>只按来源 Uid 分组，以 MAX 类型/编号导航；默认有效来源，历史物料模式保留关闭来源但仍排除删除来源。</summary>
    public async Task<IReadOnlyList<NetDemandSourceGroupDto>> SearchMaterialSourceGroupsAsync(
        MaterialDemandSourceGroupSearchRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var parameters = new PostgreSqlParameters();
        var where = LifecycleWhere(request.LifecycleStatus, parameters);
        AddTexts(request.SourceTypes, "gd.source_type", "SourceTypes", where, parameters);
        var rows = await context.Session.Connection.QueryAsync<NetDemandSourceGroupDto>(Command($"""
            SELECT gd.source_uid AS "SourceUid", MAX(gd.source_type COLLATE public.erp_dictionary_ci) AS "SourceType",
                   MAX(gd.source_no COLLATE public.erp_dictionary_ci) AS "SourceNo", COUNT(*)::integer AS "DemandCount"
            FROM "public"."net_demand" nd JOIN "public"."gross_demand" gd ON gd.uid = nd.gross_demand_id
            WHERE {string.Join(" AND ", where)}
            GROUP BY gd.source_uid
            ORDER BY MAX(gd.source_type COLLATE public.erp_dictionary_ci), MAX(gd.source_no COLLATE public.erp_dictionary_ci), gd.source_uid;
            """, parameters, cancellationToken));
        return [.. rows];
    }

    /// <summary>构造原工作台筛选，不额外要求采购目标行存在；历史实际采购过滤保留历史关联和目标头。</summary>
    private static List<string> CreateWorkspaceWhere(MaterialDemandManagerSearchRequest request, PostgreSqlParameters parameters)
    {
        var where = LifecycleWhere(request.LifecycleStatus, parameters);
        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            where.Add($"(gd.source_no COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~' OR nd.sku_snapshot COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~' OR nd.customer_sku_snapshot COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~' OR nd.description_snapshot COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~' OR pn.product_code COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~')");
            parameters.Add("Keyword", LiteralPattern(request.Keyword));
        }
        AddIds(request.NetDemandIds, "nd.uid", "NetDemandIds", where, parameters);
        AddIds(request.GrossDemandIds, "nd.gross_demand_id", "GrossDemandIds", where, parameters);
        AddIds(request.SourceUids, "gd.source_uid", "SourceUids", where, parameters);
        if (request.SourceUid.HasValue) { where.Add("gd.source_uid=@SelectedSourceUid"); parameters.Add("SelectedSourceUid", request.SourceUid.Value); }
        AddTexts(request.SourceTypes, "gd.source_type", "SourceTypes", where, parameters);
        AddTexts(request.SourceNos, "gd.source_no", "SourceNos", where, parameters);
        AddTexts(request.Skus, "nd.sku_snapshot", "Skus", where, parameters);
        AddTexts(request.CustomerSkus, "nd.customer_sku_snapshot", "CustomerSkus", where, parameters);
        if (request.SuggestedPurchaseSupplierId.HasValue)
        { where.Add("nd.suggested_purchase_supplier_id=@SuggestedPurchaseSupplierId"); parameters.Add("SuggestedPurchaseSupplierId", request.SuggestedPurchaseSupplierId.Value); }
        if (!string.IsNullOrWhiteSpace(request.SuggestedPurchaseSupplierKeyword))
        {
            where.Add($"(suggestedSupplier.supplier_code COLLATE public.erp_dictionary_ci LIKE @SuggestedSupplierKeyword ESCAPE '~' OR suggestedSupplier.supplier_name COLLATE public.erp_dictionary_ci LIKE @SuggestedSupplierKeyword ESCAPE '~')");
            parameters.Add("SuggestedSupplierKeyword", LiteralPattern(request.SuggestedPurchaseSupplierKeyword));
        }
        if (request.ActualPurchaseSupplierId.HasValue || !string.IsNullOrWhiteSpace(request.ActualPurchaseSupplierKeyword) || !string.IsNullOrWhiteSpace(request.PurchaseOrderNo))
        {
            var actual = new List<string> { "filteredLink.net_demand_id=nd.uid", $"{Text("filteredLink.target_type")}='PO' COLLATE public.erp_dictionary_ci",
                "(@LifecycleStatus<>0 OR (filteredLink.link_status=0 AND filteredLink.is_deleted=FALSE AND filteredLink.allocated_qty>filteredLink.released_qty AND filteredHead.lifecycle_status=0 AND filteredHead.is_deleted=FALSE))" };
            if (request.ActualPurchaseSupplierId.HasValue)
            { actual.Add("filteredHead.supplier_id=@ActualPurchaseSupplierId"); parameters.Add("ActualPurchaseSupplierId", request.ActualPurchaseSupplierId.Value); }
            if (!string.IsNullOrWhiteSpace(request.ActualPurchaseSupplierKeyword))
            {
                actual.Add($"(filteredSupplier.supplier_code COLLATE public.erp_dictionary_ci LIKE @ActualSupplierKeyword ESCAPE '~' OR filteredSupplier.supplier_name COLLATE public.erp_dictionary_ci LIKE @ActualSupplierKeyword ESCAPE '~')");
                parameters.Add("ActualSupplierKeyword", LiteralPattern(request.ActualPurchaseSupplierKeyword));
            }
            if (!string.IsNullOrWhiteSpace(request.PurchaseOrderNo))
            { actual.Add($"{Text("filteredHead.order_no")}={Text("@PurchaseOrderNo")}"); parameters.Add("PurchaseOrderNo", request.PurchaseOrderNo.Trim()); }
            where.Add($"""
                EXISTS (SELECT 1 FROM "public"."demand_execution_link" filteredLink JOIN "public"."purchase_order_head" filteredHead ON filteredHead.uid=filteredLink.target_head_id LEFT JOIN "public"."supplier" filteredSupplier ON filteredSupplier.uid=filteredHead.supplier_id WHERE {string.Join(" AND ", actual)})
            """);
        }
        if (request.DemandRequiredDateFrom.HasValue)
        { where.Add("nd.required_date>=@DemandRequiredDateFrom"); parameters.AddTimestamp("DemandRequiredDateFrom", request.DemandRequiredDateFrom.Value); }
        if (request.DemandRequiredDateTo.HasValue)
        { where.Add("nd.required_date<@DemandRequiredDateToExclusive"); parameters.AddTimestamp("DemandRequiredDateToExclusive", request.DemandRequiredDateTo.Value.Date.AddDays(1)); }
        if (request.IsCustomerSupplied.HasValue) where.Add(request.IsCustomerSupplied.Value ? "nd.delivery_mode=1" : "nd.delivery_mode<>1");
        if (request.IsNonStandard.HasValue) where.Add(request.IsNonStandard.Value ? "nd.product_id IS NULL" : "nd.product_id IS NOT NULL");
        return where;
    }

    /// <summary>共同生命周期边界；指定历史时放宽来源活动限制，不放宽软删除。</summary>
    private static List<string> LifecycleWhere(NetDemandLifecycleStatusEnum? status, PostgreSqlParameters parameters)
    {
        parameters.Add("LifecycleStatus", (int)(status ?? NetDemandLifecycleStatusEnum.Active));
        var where = new List<string> { "nd.lifecycle_status=@LifecycleStatus", "nd.is_deleted=FALSE", "gd.is_deleted=FALSE" };
        if (!status.HasValue || status == NetDemandLifecycleStatusEnum.Active) where.Add("gd.lifecycle_status=0");
        return where;
    }

    /// <summary>绑定去重 UUID 数组，空列表不筛选，避免 Dapper 展开与 SQL 注入。</summary>
    private static void AddIds(IEnumerable<Guid>? values, string column, string name, ICollection<string> where, PostgreSqlParameters parameters)
    {
        var array = values?.Distinct().ToArray() ?? [];
        if (array.Length == 0) return;
        where.Add($"{column}=ANY(@{name})"); parameters.AddIds(name, array);
    }

    /// <summary>绑定原样去重文本数组；仅比较表达式采用冻结 ICU 和尾 ASCII 空格，不改写传输值。</summary>
    private static void AddTexts(IEnumerable<string>? values, string column, string name, ICollection<string> where, PostgreSqlParameters parameters)
    {
        var array = values?.Distinct().ToArray() ?? [];
        if (array.Length == 0) return;
        where.Add($"EXISTS (SELECT 1 FROM unnest(@{name}) AS requested(value) WHERE {Text(column)}={Text("requested.value")})");
        parameters.AddTexts(name, array);
    }

    /// <summary>复用字面模式的空格、问号和波浪号规则，只将源转义方括号改为 PG 字面方括号。</summary>
    private static string LiteralPattern(string value) => SqlLikePattern.Build(value).Replace("~[", "[", StringComparison.Ordinal);

    /// <summary>字面 LIKE 显式采用冻结 ICU，ESCAPE 不影响原始输入的参数化边界。</summary>
    private static string Like(string column, string parameter) => $"{column} COLLATE public.erp_dictionary_ci LIKE @{parameter} ESCAPE '~'";

    /// <summary>只在等值比较中消除尾 ASCII 空格，不扩大 Unicode 规范化范围。</summary>
    private static string Text(string expression) => $"rtrim({expression}, ' ') COLLATE public.erp_dictionary_ci";

    /// <summary>每条读取借用当前会话与外层事务，允许空事务并传递取消。</summary>
    private CommandDefinition Command(string sql, object parameters, CancellationToken cancellationToken) => new(sql, parameters, context.Session.Transaction, cancellationToken: cancellationToken);


    // 工作集列固定为现有 DTO 投影；三个推荐商品关系字段原查询未读取，明确 null 而不增加新连接。
    private const string WorkspaceColumns = """
        "Uid", "Version", "LifecycleStatus", "GrossDemandId", "DecompositionRevision", "LineNumber", "CustomerId", "CustomerCode", "CustomerName", "SourceType", "SourceUid", "SourceNo", "SourceLineNumber", "SourceLineId", "RootNetDemandId", "RootSku", "IsCustomerSupplied", "DeliveryTargetNetDemandId", "ProductId", "IsNonStandard", "Sku", "LinkSku", "CustomerSku", "Description", "Color", "Specification", "ImagePath", "MaterialType", "Unit", "SupplyType", "SuggestedPurchaseSupplierId", "SuggestedPurchaseSupplierCode", "SuggestedPurchaseSupplierName", "SuggestedDeliverySupplierId", "SuggestedDeliverySupplierCode", "SuggestedDeliverySupplierName", "SuggestedDeliverySupplierHint", "GrossQty", "NetQty", "DemandRequiredDate", "AssociatedPurchaseQty", "PurchaseOrderNos", "ActualPurchaseSuppliers", "ProcurementStatus", "DueHint", "ReviewReason", "SuggestedSupplierSku", "SuggestedUnitPrice", "SuggestedTaxPrice"
        """;

    /// <summary>接收同一工作集的总量与局部 JSON 页面，空页不丢总数，公共契约仍使用原分页 DTO。</summary>
    private sealed class WorkspaceQueryRow
    {
        public int TotalCount { get; set; }
        public string ItemsJson { get; set; } = "[]";
    }
}
