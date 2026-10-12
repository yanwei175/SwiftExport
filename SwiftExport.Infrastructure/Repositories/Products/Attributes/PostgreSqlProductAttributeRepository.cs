using Dapper;
using Npgsql;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Products.Attributes;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces;
using SwiftExport.Core.Interfaces.Products.Attributes;
using SwiftExport.Infrastructure.Contexts;

namespace SwiftExport.Infrastructure.Repositories.Products.Attributes;

/// <summary>标签三表的 PostgreSQL 读写；基础写入委托共享会话的通用仓储。</summary>
public sealed class PostgreSqlProductAttributeRepository(RepositoryBaseContext context,
    IRepository<ProductAttributeGroup> groups, IRepository<ProductAttributeOption> options,
    IRepository<ProductMasterAttributeSelection> selections) : IProductAttributeRepository
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<ProductAttributeGroupDto>> GetDefinitionsAsync(Guid? categoryId, CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, ProductAttributeGroupDto>();
        // 一次联表查询避免组与选项来自不同提交；左联接允许暂时没有选项的组进入缓存。
        await context.Session.Connection.QueryAsync<ProductAttributeGroupDto, ProductAttributeOptionDto, ProductAttributeGroupDto>(
            new CommandDefinition("""
                SELECT g.uid AS "Uid", g.version AS "Version", g.category_id AS "CategoryId",
                       g.group_code AS "GroupCode", g.group_name AS "GroupName", g.selection_mode AS "SelectionMode",
                       g.sort_order AS "SortOrder", o.uid AS "Uid", o.version AS "Version",
                       o.attribute_group_id AS "AttributeGroupId", o.option_code AS "OptionCode",
                       o.option_name AS "OptionName", o.sort_order AS "SortOrder"
                FROM public.product_attribute_group g
                INNER JOIN public.product_category c ON c.uid=g.category_id AND c.is_deleted=FALSE
                LEFT JOIN public.product_attribute_option o ON o.attribute_group_id=g.uid AND o.is_deleted=FALSE
                WHERE g.is_deleted=FALSE AND (@CategoryId::uuid IS NULL OR g.category_id=@CategoryId::uuid)
                ORDER BY g.sort_order,g.uid,o.sort_order,o.uid
                """, new { CategoryId = categoryId }, context.Session.Transaction, cancellationToken: cancellationToken),
            (group, option) =>
            {
                if (!result.TryGetValue(group.Uid, out var current)) result.Add(group.Uid, current = group);
                if (option is not null) current.Options.Add(option);
                return current;
            }, splitOn: "Uid");
        return result.Values.ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ProductAttributeGroup>> GetGroupsAsync(Guid? categoryId, bool forUpdate, CancellationToken cancellationToken)
    {
        var sql = """
            SELECT g.uid AS "Uid", g.version AS "Version", g.is_deleted AS "IsDeleted",
                   g.created_date AS "CreatedDate", g.created_by AS "CreatedBy",
                   g.updated_date AS "UpdatedDate", g.updated_by AS "UpdatedBy",
                   g.category_id AS "CategoryId", g.group_code AS "GroupCode", g.group_name AS "GroupName",
                   g.selection_mode AS "SelectionMode", g.sort_order AS "SortOrder"
            FROM public.product_attribute_group g
            WHERE g.is_deleted=FALSE
            """;
        if (categoryId.HasValue) sql += " AND g.category_id=@CategoryId";
        sql += " ORDER BY g.uid";
        if (forUpdate) sql += " FOR UPDATE OF g";
        return (await context.Session.Connection.QueryAsync<ProductAttributeGroup>(new CommandDefinition(
            sql, new { CategoryId = categoryId }, context.Session.Transaction, cancellationToken: cancellationToken))).AsList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ProductAttributeOption>> GetOptionsAsync(IEnumerable<Guid> groupIds, bool forUpdate, CancellationToken cancellationToken)
    {
        var ids = groupIds.Distinct().ToArray();
        if (ids.Length == 0) return [];
        var sql = """
            SELECT o.uid AS "Uid", o.version AS "Version", o.is_deleted AS "IsDeleted",
                   o.created_date AS "CreatedDate", o.created_by AS "CreatedBy",
                   o.updated_date AS "UpdatedDate", o.updated_by AS "UpdatedBy",
                   o.attribute_group_id AS "AttributeGroupId", o.option_code AS "OptionCode",
                   o.option_name AS "OptionName", o.sort_order AS "SortOrder"
            FROM public.product_attribute_option o
            WHERE o.is_deleted=FALSE AND o.attribute_group_id=ANY(@Ids)
            ORDER BY o.uid
            """;
        if (forUpdate) sql += " FOR UPDATE OF o";
        return (await context.Session.Connection.QueryAsync<ProductAttributeOption>(new CommandDefinition(
            sql, new { Ids = ids }, context.Session.Transaction, cancellationToken: cancellationToken))).AsList();
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ProductMasterAttributeSelection>> GetSelectionsAsync(IEnumerable<Guid> masterIds, CancellationToken cancellationToken) =>
        ReadSelectionsAsync(masterIds, cancellationToken);

    /// <summary>选择集合在父分类与主型号锁下使用，原始身份不随联表缺失而消失。</summary>
    private async Task<IReadOnlyList<ProductMasterAttributeSelection>> ReadSelectionsAsync(IEnumerable<Guid> masterIds, CancellationToken cancellationToken)
    {
        var ids = masterIds.Distinct().ToArray();
        if (ids.Length == 0) return [];
        return (await context.Session.Connection.QueryAsync<ProductMasterAttributeSelection>(new CommandDefinition("""
            SELECT s.uid AS "Uid", s.version AS "Version", s.is_deleted AS "IsDeleted",
                   s.product_master_id AS "ProductMasterId", s.option_id AS "OptionId"
            FROM public.product_master_attribute_selection s
            WHERE s.is_deleted=FALSE AND s.product_master_id=ANY(@Ids) ORDER BY s.uid
            """, new { Ids = ids }, context.Session.Transaction, cancellationToken: cancellationToken))).AsList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ProductMasterAttributeSelectionDto>> GetSelectionDetailsAsync(IEnumerable<Guid> masterIds, CancellationToken cancellationToken)
    {
        var ids = masterIds.Distinct().ToArray();
        if (ids.Length == 0) return [];
        // LEFT JOIN 保留失效引用，用可识别状态提示，不能把失效关系解释为用户清空。
        return (await context.Session.Connection.QueryAsync<ProductMasterAttributeSelectionDto>(new CommandDefinition("""
            SELECT s.product_master_id AS "ProductMasterId", s.option_id AS "OptionId",
                   coalesce(o.attribute_group_id,'00000000-0000-0000-0000-000000000000'::uuid) AS "AttributeGroupId",
                   coalesce(g.category_id,'00000000-0000-0000-0000-000000000000'::uuid) AS "CategoryId",
                   coalesce(g.group_name,'标签组已失效') AS "GroupName", coalesce(o.option_name,'标签值已失效') AS "OptionName",
                   coalesce(g.selection_mode,0) AS "SelectionMode", coalesce(g.sort_order,0) AS "GroupSortOrder",
                   coalesce(o.sort_order,0) AS "OptionSortOrder",
                   (o.uid IS NOT NULL AND o.is_deleted=FALSE AND g.uid IS NOT NULL AND g.is_deleted=FALSE
                    AND c.uid IS NOT NULL AND c.is_deleted=FALSE AND g.category_id=m.category_id) AS "IsAvailable"
            FROM public.product_master_attribute_selection s
            INNER JOIN public.product_master m ON m.uid=s.product_master_id AND m.is_deleted=FALSE
            LEFT JOIN public.product_attribute_option o ON o.uid=s.option_id
            LEFT JOIN public.product_attribute_group g ON g.uid=o.attribute_group_id
            LEFT JOIN public.product_category c ON c.uid=g.category_id
            WHERE s.is_deleted=FALSE AND s.product_master_id=ANY(@Ids)
            ORDER BY g.sort_order,g.uid,o.sort_order,o.uid,s.uid
            """, new { Ids = ids }, context.Session.Transaction, cancellationToken: cancellationToken))).AsList();
    }

    /// <inheritdoc />
    public Task<int> CountReferencedMastersAsync(IEnumerable<Guid> optionIds, CancellationToken cancellationToken)
    {
        var ids = optionIds.Distinct().ToArray();
        if (ids.Length == 0) return Task.FromResult(0);
        return context.Session.Connection.ExecuteScalarAsync<int>(new CommandDefinition("""
            SELECT count(DISTINCT s.product_master_id)
            FROM public.product_master_attribute_selection s
            INNER JOIN public.product_master m ON m.uid=s.product_master_id AND m.is_deleted=FALSE
            WHERE s.is_deleted=FALSE AND s.option_id=ANY(@Ids)
            """, new { Ids = ids }, context.Session.Transaction, cancellationToken: cancellationToken));
    }

    /// <inheritdoc />
    public Task<int> CountMultipleSelectionsAsync(Guid groupId, CancellationToken cancellationToken) =>
        context.Session.Connection.ExecuteScalarAsync<int>(new CommandDefinition("""
            SELECT count(*) FROM (
                SELECT s.product_master_id
                FROM public.product_master_attribute_selection s
                INNER JOIN public.product_master m ON m.uid=s.product_master_id AND m.is_deleted=FALSE
                INNER JOIN public.product_attribute_option o ON o.uid=s.option_id AND o.is_deleted=FALSE
                WHERE s.is_deleted=FALSE AND o.attribute_group_id=@GroupId
                GROUP BY s.product_master_id HAVING count(*)>1
            ) multiple_choices
            """, new { GroupId = groupId }, context.Session.Transaction, cancellationToken: cancellationToken));

    /// <inheritdoc />
    public Task<bool> HasGroupsAsync(IEnumerable<Guid> categoryIds, CancellationToken cancellationToken)
    {
        var ids = categoryIds.Distinct().ToArray();
        if (ids.Length == 0) return Task.FromResult(false);
        return context.Session.Connection.ExecuteScalarAsync<bool>(new CommandDefinition("""
            SELECT EXISTS(SELECT 1 FROM public.product_attribute_group WHERE is_deleted=FALSE AND category_id=ANY(@Ids))
            """, new { Ids = ids }, context.Session.Transaction, cancellationToken: cancellationToken));
    }

    /// <inheritdoc />
    public Task<RepositoryWriteResult> AddGroupAsync(ProductAttributeGroup entity) => GuardCodeAsync(() => groups.AddAsync(entity), entity, null, "product_attribute_group", "ux_product_attribute_group_category_code");
    /// <inheritdoc />
    public Task<RepositoryWriteResult> UpdateGroupAsync(ProductAttributeGroup entity) => GuardCodeAsync(() => groups.UpdateAsync(entity), entity, entity.Version, "product_attribute_group", "ux_product_attribute_group_category_code");
    /// <inheritdoc />
    public Task<RepositoryWriteResult> DeleteGroupAsync(Guid uid, int version) => groups.DeleteAsync(uid, version, true);
    /// <inheritdoc />
    public Task<RepositoryWriteResult> AddOptionAsync(ProductAttributeOption entity) => GuardCodeAsync(() => options.AddAsync(entity), entity, null, "product_attribute_option", "ux_product_attribute_option_group_code");
    /// <inheritdoc />
    public Task<RepositoryWriteResult> UpdateOptionAsync(ProductAttributeOption entity) => GuardCodeAsync(() => options.UpdateAsync(entity), entity, entity.Version, "product_attribute_option", "ux_product_attribute_option_group_code");
    /// <inheritdoc />
    public Task<RepositoryWriteResult> DeleteOptionAsync(Guid uid, int version) => options.DeleteAsync(uid, version, true);
    /// <inheritdoc />
    public Task<RepositoryWriteResult> AddSelectionAsync(ProductMasterAttributeSelection entity) => selections.AddAsync(entity);
    /// <inheritdoc />
    public Task<RepositoryWriteResult> DeleteSelectionAsync(Guid uid, int version) => selections.DeleteAsync(uid, version, true);

    /// <summary>仅分类指定编码索引的冲突，不把主键或其他约束异常误报为编码重复。</summary>
    private static async Task<RepositoryWriteResult> GuardCodeAsync(Func<Task<RepositoryWriteResult>> write, BaseEntity entity, int? oldVersion, string table, string index)
    {
        try { return await write(); }
        catch (PostgresException exception) when (exception.SqlState == "23505" && exception.SchemaName == "public"
            && exception.TableName == table && exception.ConstraintName == index)
        { return RepositoryWriteResult.Fail(entity.Uid, oldVersion, ServiceErrorCodes.Validation, "标签组或选项编码已被占用，请刷新后重试。"); }
    }
}
