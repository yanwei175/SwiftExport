using Dapper;
using SwiftExport.Contracts.DTOs.ForExcel.PurchaseOrders;
using SwiftExport.Core.Interfaces.ForExcel;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.ForExcel;

/// <summary>采购导入完整精确查询；保留全部候选及歧义，在可空调用方事务内共享锁定已有行，不提供不存在关系的范围保护。</summary>
public sealed class PostgreSqlForExcelPurchaseOrderRepository(RepositoryBaseContext context) : IForExcelPurchaseOrderRepository
{
    /// <summary>透传原可空事务和取消令牌；不新建连接、事务或强制父事务门槛。</summary>
    private CommandDefinition Command(string sql, object args, CancellationToken token) =>
        new(sql, args, context.Session.Transaction, cancellationToken: token);

    /// <summary>输入沿用完整 Trim 和大写，数据库仅修剪 ASCII 空格；返回全部未删客户，包括停用或歧义候选。</summary>
    public async Task<IReadOnlyList<PurchaseOrderImportCustomerDto>> FindCustomersAsync(string code, CancellationToken token) =>
        (await context.Session.Connection.QueryAsync<PurchaseOrderImportCustomerDto>(Command($"""
            SELECT uid AS "Uid",customer_code AS "CustomerCode",customer_name AS "CustomerName",is_active AS "IsActive"
            FROM public.customer
            WHERE is_deleted=FALSE AND upper(btrim(customer_code,' ')) COLLATE public.erp_dictionary_ci=@Code
            ORDER BY {UuidOrder("uid")} FOR SHARE;
            """, new { Code = code.Trim().ToUpperInvariant() }, token))).AsList();

    /// <summary>按原稳定身份读取未删供应商并共享锁定；保留停用结果供服务判断资格。</summary>
    public Task<PurchaseOrderImportSupplierDto?> FindSupplierAsync(Guid uid, CancellationToken token) =>
        context.Session.Connection.QuerySingleOrDefaultAsync<PurchaseOrderImportSupplierDto>(Command("""
            SELECT uid AS "Uid",supplier_code AS "SupplierCode",supplier_name AS "SupplierName",is_active AS "IsActive"
            FROM public.supplier WHERE uid=@uid AND is_deleted=FALSE FOR SHARE;
            """, new { uid }, token));

    /// <summary>按客户和原输入数组返回所有关联；保留断链和不可采购候选，资格只写入 IsUsable，不删候选。</summary>
    public async Task<IReadOnlyList<PurchaseOrderImportProductCandidateDto>> MatchProductsAsync(Guid customerId, string[] skus, CancellationToken token)
    {
        // 空数组与源一致先返回，包括已取消请求；不再规范化、去重或修改调用方数组。
        if (skus.Length == 0) return [];
        var parameters = new PostgreSqlParameters();
        parameters.Add("CustomerId", customerId);
        parameters.AddTexts("Skus", skus);
        // 产品锁置于固定 LATERAL 源内，外层只锁非空关系；不能 FOR SHARE OF 可空产品侧。
        return (await context.Session.Connection.QueryAsync<PurchaseOrderImportProductCandidateDto>(Command($"""
            SELECT cp.uid AS "CustomerProductId",cp.customer_sku AS "CustomerSku",p.uid AS "ProductId",p.sku AS "Sku",
                   COALESCE(NULLIF(btrim(cp.link_sku,' '),''),p.link_sku) AS "LinkSku",
                   p.unit AS "Unit",p.main_image AS "MainImage",p.material_type AS "MaterialType",
                   CASE WHEN cp.is_active=TRUE AND p.is_active=TRUE
                     AND rtrim(p.supply_type,' ') COLLATE public.erp_dictionary_ci='采购'
                     AND rtrim(p.material_type,' ') COLLATE public.erp_dictionary_ci IN ('成品','零件','客供件')
                     AND NULLIF(btrim(p.sku,' '),'') IS NOT NULL THEN TRUE ELSE FALSE END AS "IsUsable"
            FROM public.customer_product cp
            LEFT JOIN LATERAL (
                SELECT source.uid,source.sku,source.link_sku,source.unit,source.main_image,
                       source.material_type,source.supply_type,source.is_active
                FROM public.product source WHERE source.uid=cp.sku_id AND source.is_deleted=FALSE FOR SHARE
            ) p ON TRUE
            WHERE cp.customer_id=@CustomerId AND cp.is_deleted=FALSE
              AND upper(btrim(cp.customer_sku,' ')) COLLATE public.erp_dictionary_ci=ANY(
                  SELECT rtrim(input.sku,' ') FROM unnest(@Skus) input(sku))
            ORDER BY rtrim(cp.customer_sku,' ') COLLATE public.erp_dictionary_ci NULLS FIRST,{UuidOrder("cp.uid")}
            FOR SHARE OF cp;
            """, parameters, token))).AsList();
    }

    /// <summary>共享锁定全部匹配且启用的单位行，只在恰好一条时返回原修剪后的名称；缺失或歧义返回空。</summary>
    public async Task<string?> FindUnitAsync(string unit, CancellationToken token)
    {
        var items = (await context.Session.Connection.QueryAsync<string>(Command($"""
            SELECT btrim(data_name_en,' ') FROM public.sys_dict_data
            WHERE rtrim(dict_code,' ') COLLATE public.erp_dictionary_ci='Unit' AND is_enabled=TRUE AND is_deleted=FALSE
              AND upper(btrim(data_name_en,' ')) COLLATE public.erp_dictionary_ci=@Unit
            ORDER BY {UuidOrder("uid")} FOR SHARE;
            """, new { Unit = unit.Trim().ToUpperInvariant() }, token))).AsList();
        return items.Count == 1 ? items[0] : null;
    }

    /// <summary>复用已冻结的 SqlGuid 字节优先级，保留源 uniqueidentifier 排序，不使用 PG 原生 UUID 尾序。</summary>
    private static string UuidOrder(string column) => "ARRAY[" + string.Join(',',
        new[] { 10, 11, 12, 13, 14, 15, 8, 9, 7, 6, 5, 4, 3, 2, 1, 0 }.Select(index => $"get_byte(uuid_send({column}),{index})")) + "]";
}
