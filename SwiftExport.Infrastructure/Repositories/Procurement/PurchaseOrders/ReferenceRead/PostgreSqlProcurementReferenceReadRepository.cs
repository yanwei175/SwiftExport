using Dapper;
using SwiftExport.Contracts.DTOs.MasterData.ProcurementLookups;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Entities.Relations;
using SwiftExport.Core.Entities.Systems.Dictionaries.Data;
using SwiftExport.Core.Interfaces.Procurement;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.Procurement.PurchaseOrders.ReferenceRead;

/// <summary>在调用方 PG 事务内读取采购主数据资格与快照；保护已读行，不改变原资格或补造缺失数据。</summary>
public sealed class PostgreSqlProcurementReferenceReadRepository(RepositoryBaseContext context) : IProcurementReferenceReadRepository
{
    /// <summary>读取原客户五字段及未删资格，对已有记录取得共享行锁。</summary>
    public Task<Customer?> GetCustomerAsync(Guid uid, CancellationToken ct) => context.Session.Connection.QuerySingleOrDefaultAsync<Customer>(Command(
        """
            SELECT t."uid" AS "Uid",
            t."version" AS "Version",
            t."is_active" AS "IsActive",
            t."customer_code" AS "CustomerCode",
            t."customer_name" AS "CustomerName" FROM "public"."customer" t
            WHERE t."uid"=@uid AND COALESCE(t."is_deleted",FALSE)=FALSE FOR SHARE OF t;
        """, new { uid }, ct));

    /// <summary>读取原供应商五字段，不在仓储新增活动校验。</summary>
    public Task<Supplier?> GetSupplierAsync(Guid uid, CancellationToken ct) => context.Session.Connection.QuerySingleOrDefaultAsync<Supplier>(Command(
        """
            SELECT t."uid" AS "Uid",
            t."version" AS "Version",
            t."is_active" AS "IsActive",
            t."supplier_code" AS "SupplierCode",
            t."supplier_name" AS "SupplierName" FROM "public"."supplier" t
            WHERE t."uid"=@uid AND COALESCE(t."is_deleted",FALSE)=FALSE FOR SHARE OF t;
        """, new { uid }, ct));

    /// <summary>读取产品执行属性和当前 BOM 身份，缺失返回空，由原服务判断资格。</summary>
    public Task<Product?> GetProductAsync(Guid uid, CancellationToken ct) => context.Session.Connection.QuerySingleOrDefaultAsync<Product>(Command(
        """
            SELECT t."uid" AS "Uid",
            t."version" AS "Version",
            t."is_active" AS "IsActive",
            t."sku" AS "Sku",
            t."description_zh" AS "DescriptionZh",
            t."unit" AS "Unit",
            t."material_type" AS "MaterialType",
            t."supply_type" AS "SupplyType",
            t."bom_no" AS "BomNo",
            t."bom_version" AS "BomVersion" FROM "public"."product" t
            WHERE t."uid"=@uid AND COALESCE(t."is_deleted",FALSE)=FALSE FOR SHARE OF t;
        """, new { uid }, ct));

    /// <summary>保留产品、颜色及首条客户/供应商关系快照；外连缺失仍返回空文本，不新增关系资格。</summary>
    public Task<ProcurementProductCandidateDto?> GetProductCandidateAsync(Guid productId, Guid customerId, Guid supplierId, CancellationToken ct)
    {
        var sql = $"""
            SELECT p."uid" AS "Uid",p."sku" AS "Sku",
                COALESCE(p."main_image",'') AS "MainImage",COALESCE(cp."CustomerSku",'') AS "CustomerSku",
                COALESCE(p."link_sku",'') AS "LinkSku",COALESCE(sp."SupplierSku",'') AS "SupplierSku",
                COALESCE(p."description_zh",'') AS "DescriptionZh",COALESCE(p."description_en",'') AS "DescriptionEn",
                COALESCE(color."ColorNameZh",'') AS "FinishZh",COALESCE(p."features_zh",'') AS "FeaturesZh",
                COALESCE(p."material_type",'') AS "MaterialType",COALESCE(p."supply_type",'') AS "SupplyType",
                COALESCE(p."unit",'') AS "Unit"
            FROM "public"."product" p
            LEFT JOIN LATERAL (
                SELECT c."color_name_zh" AS "ColorNameZh" FROM "public"."product_color" c
                WHERE c."uid"=p."color_id" AND COALESCE(c."is_deleted",FALSE)=FALSE FOR SHARE OF c
            ) color ON TRUE
            LEFT JOIN LATERAL (
                SELECT r."customer_sku" AS "CustomerSku" FROM "public"."customer_product" r
                WHERE r."customer_id"=@customerId AND r."sku_id"=p."uid"
                    AND r."is_active"=TRUE AND COALESCE(r."is_deleted",FALSE)=FALSE
                ORDER BY {UuidOrder("r.\"uid\"")} LIMIT 1 FOR SHARE OF r
            ) cp ON TRUE
            LEFT JOIN LATERAL (
                SELECT r."supplier_sku" AS "SupplierSku" FROM "public"."supplier_product" r
                WHERE r."supplier_id"=@supplierId AND r."sku_id"=p."uid"
                    AND COALESCE(r."is_deleted",FALSE)=FALSE
                ORDER BY {UuidOrder("r.\"uid\"")} LIMIT 1 FOR SHARE OF r
            ) sp ON TRUE
            WHERE p."uid"=@productId AND p."is_active"=TRUE
                AND COALESCE(p."is_deleted",FALSE)=FALSE FOR SHARE OF p;
            """;
        // FOR SHARE 放在各非空源查询内，避免锁 LEFT JOIN 的可空侧；不存在的关系不假称已得到 PG 行锁。
        return context.Session.Connection.QuerySingleOrDefaultAsync<ProcurementProductCandidateDto>(Command(sql, new { productId, customerId, supplierId }, ct));
    }

    /// <summary>原单位输入只转大写，存储值仅修剪 ASCII 空格；启用匹配恰好一条才返回规范值。</summary>
    public async Task<string?> GetUnitAsync(string unit, CancellationToken ct)
    {
        var sql = $"""
            SELECT btrim(d."data_name_en",' ') FROM "public"."sys_dict_data" d
            WHERE rtrim(d."dict_code",' ') COLLATE public.erp_dictionary_ci='Unit'
                AND d."is_enabled"=TRUE AND COALESCE(d."is_deleted",FALSE)=FALSE
                AND upper(btrim(d."data_name_en",' ')) COLLATE public.erp_dictionary_ci
                    =rtrim(upper(@unit),' ') COLLATE public.erp_dictionary_ci FOR SHARE OF d;
            """;
        var values = (await context.Session.Connection.QueryAsync<string>(Command(sql, new { unit }, ct))).AsList();
        return values.Count == 1 ? values[0] : null;
    }

    /// <summary>沿源采购持久化事务要求和取消令牌，不替调用方提交或回滚。</summary>
    private CommandDefinition Command(string sql, object args, CancellationToken ct) => new(sql, args, context.Session.Transaction ?? throw new InvalidOperationException("采购持久化需要事务。"), cancellationToken: ct);

    /// <summary>保持 SQL Server uniqueidentifier 选择首条关系时的排序权重。</summary>
    private static string UuidOrder(string column) => "ARRAY[" + string.Join(',', new[] { 10, 11, 12, 13, 14, 15, 8, 9, 7, 6, 5, 4, 3, 2, 1, 0 }.Select(index => $"get_byte(uuid_send({column}),{index})")) + "]";
}
