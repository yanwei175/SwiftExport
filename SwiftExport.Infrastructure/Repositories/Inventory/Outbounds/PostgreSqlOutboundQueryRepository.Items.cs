using System.Data;
using Dapper;
using Npgsql;
using NpgsqlTypes;
using SwiftExport.Contracts.DTOs.Inventory.Outbounds;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Inventory;
using SwiftExport.Core.Interfaces.Inventory.Outbounds;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.Inventory.Outbounds;

/// <summary>补齐出库正库存选品；同一次物化候选供计数与分页，原五方法接口完整但运行接线另行验收。</summary>
public sealed partial class PostgreSqlOutboundQueryRepository : IOutboundQueryRepository
{

    /// <summary>固定一次候选观察并只传输当前页；空页仍返回总数，资格及数量由SQL决定，不创建事务或预占库存。</summary>
    public async Task<PagedResponseBase<OutboundItemCandidateDto>> SearchItemsAsync(OutboundItemSearchRequest request, CancellationToken token)
    {
        // null列表按已核实的源Dapper空集合结果翻译；保留非空数组顺序、重复与Empty，不影响服务null拒绝。
        var parameters = new ItemQueryParameters(request.WarehouseSiteId, request.ExcludedProductIds?.ToArray() ?? [],
            Like(request.Keyword), (long)(request.PageIndex - 1) * request.PageSize, request.PageSize);
        var rows = (await context.Session.Connection.QueryAsync<ItemQueryRow>(Command(ItemsSql(), parameters, token))).ToList();
        var result = new PagedResponseBase<OutboundItemCandidateDto>
        { TotalCount = rows.First().TotalCount, PageIndex = request.PageIndex, PageSize = request.PageSize };
        foreach (var row in rows)
        {
            if (!row.HasItem) continue;
            // LEFT JOIN占位只能由HasItem判断；真实行缺必需字段暴露映射错误，不能默默补Empty或零余额。
            result.Items.Add(new()
            {
                Uid = row.Uid!.Value,
                WarehouseSiteId = row.WarehouseSiteId!.Value,
                ProductId = row.ProductId!.Value,
                Sku = row.Sku,
                LinkSku = row.LinkSku,
                Description = row.Description,
                ImagePath = row.ImagePath,
                Unit = row.Unit,
                OnHandQty = row.OnHandQty!.Value,
                DefaultWarehouseName = row.DefaultWarehouseName
            });
        }
        return result;
    }

    /// <summary>单语句物化原十字段，默认仓仅排软删；单位ASCII过滤不改变投影，局部负LIMIT保持原非法FETCH技术失败。</summary>
    private static string ItemsSql() => $"""
        WITH candidates AS MATERIALIZED (
            SELECT b."uid" AS "Uid",b."warehouse_site_id" AS "WarehouseSiteId",
                b."product_id" AS "ProductId",{P("Sku")} AS "Sku",pn.product_code AS "LinkSku",
                {P("DescriptionZh")} AS "Description",{P("MainImage")} AS "ImagePath",{P("Unit")} AS "Unit",
                b."on_hand_qty" AS "OnHandQty",dw."name" AS "DefaultWarehouseName"
            FROM "public"."inventory_balance" b JOIN "public"."product" p ON {P("Uid")}=b."product_id"
            JOIN "public"."warehouse_site" w ON w."uid"=b."warehouse_site_id"
            LEFT JOIN public.product_neutral pn ON pn.uid=p.product_neutral_id AND pn.is_deleted=FALSE
            LEFT JOIN "public"."warehouse_site" dw ON dw."uid"={P("DefaultWarehouseSiteId")}
                AND dw."is_deleted"=FALSE
            WHERE b."warehouse_site_id"=@WarehouseSiteId AND b."is_deleted"=FALSE
                AND b."on_hand_qty">0 AND {P("IsDeleted")}=FALSE AND {P("IsActive")}=TRUE
                AND w."is_deleted"=FALSE AND w."is_active"=TRUE
                AND NULLIF(btrim({P("Unit")},' '),'') IS NOT NULL
                AND b."product_id"<>ALL(@ExcludedProductIds)
                AND (@Keyword IS NULL OR {P("Sku")} COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~'
                    OR pn.product_code COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~'
                    OR {P("DescriptionZh")} COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~')
        ), totals AS (
            SELECT COUNT(*)::integer AS "TotalCount" FROM candidates
        ), page AS (
            SELECT c.*,TRUE AS "HasItem" FROM candidates c ORDER BY {ItemOrder("c")}
            LIMIT CASE WHEN @Take<=0 THEN -1 ELSE @Take END OFFSET @Skip
        )
        SELECT t."TotalCount",COALESCE(pg."HasItem",FALSE) AS "HasItem",pg."Uid",pg."WarehouseSiteId",pg."ProductId",
            pg."Sku",pg."LinkSku",pg."Description",pg."ImagePath",pg."Unit",pg."OnHandQty",pg."DefaultWarehouseName"
        FROM totals t LEFT JOIN page pg ON TRUE ORDER BY {ItemOrder("pg")};
        """;

    /// <summary>页内与最终传输都使用冻结的SKU局部尾空格键及产品SqlGuid尾序，不修改返回SKU或在C#排序。</summary>
    private static string ItemOrder(string alias) => $"rtrim({alias}.\"Sku\",' ') COLLATE public.erp_dictionary_ci ASC NULLS FIRST,{UuidOrder(alias + ".\"ProductId\"")} ASC NULLS FIRST";

    /// <summary>只用于单结果传输包；LEFT JOIN占位身份/余额可空，文本NULL沿原DTO语义。</summary>
    private sealed class ItemQueryRow
    {
        public int TotalCount { get; set; }
        public bool HasItem { get; set; }
        public Guid? Uid { get; set; }
        public Guid? WarehouseSiteId { get; set; }
        public Guid? ProductId { get; set; }
        public string? Sku { get; set; }
        public string? LinkSku { get; set; }
        public string? Description { get; set; }
        public string? ImagePath { get; set; }
        public string? Unit { get; set; }
        public decimal? OnHandQty { get; set; }
        public string? DefaultWarehouseName { get; set; }
    }

    /// <summary>本方法独立绑定五参数，避免四查询参数装饰器把新UUID/数组识别为Integer，不改变公共组件。</summary>
    private sealed class ItemQueryParameters(Guid warehouse, Guid[] excluded, string? keyword, long skip, int take) : SqlMapper.IDynamicParameters
    {
        /// <summary>每命令创建独立Npgsql参数；NULL关键词显式text，数组作为一个uuid[]不展开。</summary>
        public void AddParameters(IDbCommand command, SqlMapper.Identity identity)
        {
            command.Parameters.Add(new NpgsqlParameter("WarehouseSiteId", NpgsqlDbType.Uuid) { Value = warehouse });
            command.Parameters.Add(new NpgsqlParameter("ExcludedProductIds", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = excluded });
            command.Parameters.Add(new NpgsqlParameter("Keyword", NpgsqlDbType.Text) { Value = (object?)keyword ?? DBNull.Value });
            command.Parameters.Add(new NpgsqlParameter("Skip", NpgsqlDbType.Bigint) { Value = skip });
            command.Parameters.Add(new NpgsqlParameter("Take", NpgsqlDbType.Integer) { Value = take });
        }
    }
}
