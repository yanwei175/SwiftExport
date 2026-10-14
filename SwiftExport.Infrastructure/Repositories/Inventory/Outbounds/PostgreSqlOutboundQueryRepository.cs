using System.Data;
using Dapper;
using Npgsql;
using NpgsqlTypes;
using SwiftExport.Contracts.DTOs.Inventory.Outbounds;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Inventory;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.Inventory.Outbounds;

/// <summary>出库五方法查询仓储；普通读取在本文件，选品在独立partial一次物化，完整接口的运行DI另行验收。</summary>
public sealed partial class PostgreSqlOutboundQueryRepository(RepositoryBaseContext context)
{
    private static readonly EntityMetadata ProductMap = EntityMetadataCache.GetMetadata(typeof(Product));

    /// <summary>一次多结果分别读取当前计数与页；原分页值和SQL技术失败保留，不新增生命周期或自动单过滤。</summary>
    public async Task<PagedResponseBase<OutboundManagerListItemDto>> SearchAsync(OutboundManagerSearchRequest request, CancellationToken token)
    {
        var p = new PostgreSqlParameters();
        p.Add("DocumentType", request.DocumentType.HasValue ? (int)request.DocumentType.Value : null);
        p.Add("PostingStatus", request.PostingStatus.HasValue ? (int)request.PostingStatus.Value : null);
        p.Add("Keyword", Like(request.Keyword));
        // 减1先在int中计算再提升long，与源表达式相同；不裁剪原页号或页大小。
        p.Add("Skip", (long)(request.PageIndex - 1) * request.PageSize);
        p.Add("Take", request.PageSize);
        using var reader = await context.Session.Connection.QueryMultipleAsync(Command(SearchSql(), new QueryParameters(p), token));
        return new() { TotalCount = await reader.ReadSingleAsync<int>(), Items = (await reader.ReadAsync<OutboundManagerListItemDto>()).ToList(), PageIndex = request.PageIndex, PageSize = request.PageSize };
    }

    /// <summary>头只未删，行仍独立联接头身份并全缓冲后判缺头；当前产品不额外排删除，余额缺失回零。</summary>
    public async Task<OutboundManagerDetailDto?> GetDetailAsync(Guid uid, CancellationToken token)
    {
        var p = new PostgreSqlParameters(); p.Add("Uid", uid);
        using var reader = await context.Session.Connection.QueryMultipleAsync(Command(DetailSql(), new QueryParameters(p), token));
        var header = await reader.ReadSingleOrDefaultAsync<OutboundHeaderDetailDto>();
        var lines = (await reader.ReadAsync<OutboundLineDetailDto>()).ToList();
        return header is null ? null : new() { Header = header, Lines = lines };
    }

    /// <summary>读取未删物料及其独立型号；不依赖中性产品匹配，不筛停用或余额，空集合沿用短路。</summary>
    public async Task<IReadOnlyList<OutboundItemCandidateDto>> GetProductsAsync(IReadOnlyList<Guid> ids, CancellationToken token)
    {
        if (ids.Count == 0) return [];
        var p = new PostgreSqlParameters(); p.AddIds("Ids", ids);
        return (await context.Session.Connection.QueryAsync<OutboundItemCandidateDto>(Command($"""
            SELECT {P("Uid")} AS "ProductId",{P("Sku")} AS "Sku",p.link_sku AS "LinkSku",
                {P("DescriptionZh")} AS "Description",{P("MainImage")} AS "ImagePath",{P("Unit")} AS "Unit"
            FROM "public"."product" p
            WHERE {P("Uid")}=ANY(@Ids) AND {P("IsDeleted")}=FALSE;
            """, new QueryParameters(p), token))).ToList();
    }

    /// <summary>仅按产品身份、启用及未删判断新增引用资格，不要求单位、仓库或正库存。</summary>
    public Task<bool> IsProductActiveAsync(Guid productId, CancellationToken token)
    {
        var p = new PostgreSqlParameters(); p.Add("ProductId", productId);
        return context.Session.Connection.ExecuteScalarAsync<bool>(Command($"""
            SELECT EXISTS(SELECT 1 FROM "public"."product" p WHERE {P("Uid")}=@ProductId AND {P("IsActive")}=TRUE AND {P("IsDeleted")}=FALSE);
            """, new QueryParameters(p), token));
    }

    /// <summary>所有命令借用同会话可空事务及原令牌，不持有读写事务或执行锁。</summary>
    private CommandDefinition Command(string sql, object args, CancellationToken token) => new(sql, args, context.Session.Transaction, cancellationToken: token);

    /// <summary>同过滤的计数与页各自观察当前事实；行COUNT/SUM独立子查询避免倍增。</summary>
    private static string SearchSql()
    {
        var filter = $"""
            FROM "public"."outbound_document" h WHERE h."is_deleted"=FALSE
                AND (@DocumentType IS NULL OR h."document_type"=@DocumentType)
                AND (@PostingStatus IS NULL OR h."posting_status"=@PostingStatus)
                AND (@Keyword IS NULL OR h."document_no" COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~'
                    OR h."warehouse_name_snapshot" COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~'
                    OR EXISTS(SELECT 1 FROM "public"."outbound_document_line" l WHERE l."outbound_document_id"=h."uid"
                        AND l."is_deleted"=FALSE AND l."sku_snapshot" COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~'))
            """;
        // PG LIMIT 0会成功，但源FETCH<=0拒绝；局部SQL负LIMIT保持技术失败，不转换业务校验。
        return $"""
            SELECT COUNT(*)::integer {filter};
            SELECT h."uid" AS "Uid",h."version" AS "Version",h."document_no" AS "DocumentNo",
                h."document_type" AS "DocumentType",h."lifecycle_status" AS "LifecycleStatus",h."posting_status" AS "PostingStatus",
                h."warehouse_site_id" AS "WarehouseSiteId",h."warehouse_name_snapshot" AS "WarehouseNameSnapshot",
                h."shipment_date" AS "ShipmentDate",h."remark" AS "Remark",h."issue_reason" AS "IssueReason",
                (SELECT COUNT(*)::integer FROM "public"."outbound_document_line" l WHERE l."outbound_document_id"=h."uid" AND l."is_deleted"=FALSE) AS "LineCount",
                COALESCE((SELECT SUM(l."quantity") FROM "public"."outbound_document_line" l WHERE l."outbound_document_id"=h."uid" AND l."is_deleted"=FALSE),0) AS "TotalQuantity"
            {filter}
            ORDER BY h."shipment_date" DESC NULLS LAST,{UuidOrder("h.\"uid\"")} DESC NULLS LAST
            LIMIT CASE WHEN @Take<=0 THEN -1 ELSE @Take END OFFSET @Skip;
            """;
    }

    /// <summary>第二结果只复核行未删和头身份；头已删仍缓冲其有效行，不套其他详情的有效产品过滤。</summary>
    private static string DetailSql() => $"""
        SELECT h."uid" AS "Uid",
            h."version" AS "Version",
            h."document_no" AS "DocumentNo",
            h."document_type" AS "DocumentType",
            h."lifecycle_status" AS "LifecycleStatus",
            h."posting_status" AS "PostingStatus",
            h."warehouse_site_id" AS "WarehouseSiteId",
            h."warehouse_code_snapshot" AS "WarehouseCodeSnapshot",
            h."warehouse_name_snapshot" AS "WarehouseNameSnapshot",
            h."shipment_date" AS "ShipmentDate",
            h."remark" AS "Remark",
            h."issue_reason" AS "IssueReason",
            h."posted_date" AS "PostedDate",
            h."posted_by" AS "PostedBy",
            h."reversed_date" AS "ReversedDate",
            h."reversed_by" AS "ReversedBy",
            h."reverse_reason" AS "ReverseReason",
            h."trigger_packing_list_id" AS "TriggerPackingListId",
            h."command_id" AS "CommandId",
            h."created_date" AS "CreatedDate",
            h."created_by" AS "CreatedBy",
            h."updated_date" AS "UpdatedDate",
            h."updated_by" AS "UpdatedBy",
            h."is_deleted" AS "IsDeleted"
        FROM "public"."outbound_document" h WHERE h."uid"=@Uid AND h."is_deleted"=FALSE;
        SELECT l."uid" AS "Uid",
            l."version" AS "Version",
            l."outbound_document_id" AS "OutboundDocumentId",
            l."line_number" AS "LineNumber",
            l."product_id" AS "ProductId",
            l."sku_snapshot" AS "SkuSnapshot",
            l."description_snapshot" AS "DescriptionSnapshot",
            l."unit_snapshot" AS "UnitSnapshot",
            l."quantity" AS "Quantity",
            l."command_item_id" AS "CommandItemId",
            l."original_line_id" AS "OriginalLineId",
            l."created_date" AS "CreatedDate",
            l."created_by" AS "CreatedBy",
            l."updated_date" AS "UpdatedDate",
            l."updated_by" AS "UpdatedBy",
            p.link_sku AS "LinkSku",{P("MainImage")} AS "ImagePath",COALESCE(b."on_hand_qty",0) AS "OnHandQty"
        FROM "public"."outbound_document_line" l JOIN "public"."outbound_document" h ON h."uid"=l."outbound_document_id"
        LEFT JOIN "public"."product" p ON {P("Uid")}=l."product_id"
        LEFT JOIN "public"."inventory_balance" b ON b."warehouse_site_id"=h."warehouse_site_id"
            AND b."product_id"=l."product_id" AND b."is_deleted"=FALSE
        WHERE l."outbound_document_id"=@Uid AND l."is_deleted"=FALSE
        ORDER BY l."line_number" ASC NULLS FIRST,{UuidOrder("l.\"uid\"")} ASC NULLS FIRST;
        """;

    /// <summary>源.NET空白/Trim与字面LIKE转义原样保留，不扩展公共匹配能力。</summary>
    private static string? Like(string? value) => string.IsNullOrWhiteSpace(value) ? null : "%" + value.Trim().Replace("~", "~~").Replace("%", "~%").Replace("_", "~_").Replace("[", "~[") + "%";
    /// <summary>固定元数据与内部别名提供物理列，不接受外部标识符。</summary>
    private static string C(EntityMetadata map, string alias, string property) => alias + "." + PostgreSqlNames.Quote(map.Columns.Single(x => x.PropertyName == property).ColumnName);
    private static string P(string property) => C(ProductMap, "p", property);
    /// <summary>保留SQL Server uniqueidentifier的SqlGuid字节比较顺序。</summary>
    private static string UuidOrder(string column) => "ARRAY[" + string.Join(',', new[] { 10, 11, 12, 13, 14, 15, 8, 9, 7, 6, 5, 4, 3, 2, 1, 0 }.Select(x => $"get_byte(uuid_send({column}),{x})")) + "]";

    /// <summary>本查询局部明确NULL标量和UUID数组类型；每命令独立克隆，不修改公共参数组件。</summary>
    private sealed class QueryParameters(PostgreSqlParameters parameters) : SqlMapper.IDynamicParameters
    {
        /// <summary>装饰连接也按uuid[]单参数绑定，NULL enum/text不依赖驱动猜测。</summary>
        public void AddParameters(IDbCommand command, SqlMapper.Identity identity)
        {
            parameters.AddParameters(command, identity);
            foreach (NpgsqlParameter parameter in command.Parameters)
                parameter.NpgsqlDbType = parameter.ParameterName switch
                {
                    "Uid" or "ProductId" => NpgsqlDbType.Uuid,
                    "Ids" => NpgsqlDbType.Array | NpgsqlDbType.Uuid,
                    "Keyword" => NpgsqlDbType.Text,
                    "Skip" => NpgsqlDbType.Bigint,
                    _ => NpgsqlDbType.Integer
                };
        }
    }
}
