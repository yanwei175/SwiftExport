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

/// <summary>共享标签四表的 PostgreSQL 读写；基础写入委托同一会话的通用审计仓储。</summary>
public sealed class PostgreSqlProductAttributeRepository(RepositoryBaseContext context,
    IRepository<ProductAttributeGroup> groups, IRepository<ProductAttributeOption> options,
    IRepository<ProductMasterAttributeSelection> selections,
    IRepository<ProductCategoryAttributeGroup> relations) : IProductAttributeRepository
{
    // 路径携带已访问身份阻止异常循环；上层必须校验路径完整性，不能把断链当成正常无标签。
    private const string CategoryPathSql = """
        WITH RECURSIVE category_path AS (
            SELECT c.uid,c.parent_id,ARRAY[c.uid] AS visited
            FROM public.product_category c WHERE c.uid=@CategoryId::uuid AND c.is_deleted=FALSE
            UNION ALL
            SELECT c.uid,c.parent_id,p.visited||c.uid
            FROM category_path p
            INNER JOIN public.product_category c ON c.uid=p.parent_id AND c.is_deleted=FALSE
            WHERE NOT c.uid=ANY(p.visited)
        )
        """;

    private const string GroupColumnsSql = """
        SELECT g.uid AS "Uid",g.version AS "Version",g.is_deleted AS "IsDeleted",
               g.created_date AS "CreatedDate",g.created_by AS "CreatedBy",
               g.updated_date AS "UpdatedDate",g.updated_by AS "UpdatedBy",
               g.group_code AS "GroupCode",g.group_name AS "GroupName",
               g.selection_mode AS "SelectionMode",g.sort_order AS "SortOrder"
        FROM public.product_attribute_group g
        """;

    /// <inheritdoc />
    public async Task<ProductAttributeDefinitionsDto> GetSnapshotAsync(Guid? categoryId, CancellationToken cancellationToken)
    {
        var snapshot = new ProductAttributeDefinitionsDto { CategoryId = categoryId ?? Guid.Empty };
        var groupMap = new Dictionary<Guid, ProductAttributeGroupDto>();
        var optionIds = new HashSet<Guid>();
        var relationIds = new HashSet<Guid>();
        var hierarchyValid = true;
        // 一条联表语句提供同一提交快照；无分类关联的全局组也保留，交叉联接产生的重复身份在此去重。
        await context.Session.Connection.QueryAsync<SnapshotStatus, ProductAttributeGroupDto, ProductAttributeOptionDto,
            ProductCategoryAttributeGroupDto, SnapshotStatus>(new CommandDefinition(CategoryPathSql + """
            , eligible_relations AS (
                SELECT r.uid,r.version,r.category_id,r.attribute_group_id
                FROM public.product_category_attribute_group r
                INNER JOIN public.product_category c ON c.uid=r.category_id AND c.is_deleted=FALSE
                WHERE r.is_deleted=FALSE
                  AND (@CategoryId::uuid IS NULL OR r.category_id IN (SELECT uid FROM category_path))
            ), eligible_groups AS (
                SELECT g.uid,g.version,g.group_code,g.group_name,g.selection_mode,g.sort_order
                FROM public.product_attribute_group g
                WHERE g.is_deleted=FALSE AND (@CategoryId::uuid IS NULL
                    OR g.uid IN (SELECT attribute_group_id FROM eligible_relations))
            )
            SELECT (@CategoryId::uuid IS NULL OR EXISTS(SELECT 1 FROM category_path
                WHERE parent_id IS NULL OR parent_id='00000000-0000-0000-0000-000000000000'::uuid)) AS "HierarchyValid",
                   g.uid AS "Uid",g.version AS "Version",g.group_code AS "GroupCode",g.group_name AS "GroupName",
                   g.selection_mode AS "SelectionMode",g.sort_order AS "SortOrder",
                   o.uid AS "Uid",o.version AS "Version",o.attribute_group_id AS "AttributeGroupId",
                   o.option_code AS "OptionCode",o.option_name AS "OptionName",o.sort_order AS "SortOrder",
                   r.uid AS "Uid",r.version AS "Version",r.category_id AS "CategoryId",r.attribute_group_id AS "AttributeGroupId"
            FROM (SELECT 1) anchor
            LEFT JOIN eligible_groups g ON TRUE
            LEFT JOIN public.product_attribute_option o ON o.attribute_group_id=g.uid AND o.is_deleted=FALSE
            LEFT JOIN eligible_relations r ON r.attribute_group_id=g.uid
            ORDER BY g.sort_order,g.uid,o.sort_order,o.uid,r.category_id,r.uid
            """, new { CategoryId = categoryId }, context.Session.Transaction, cancellationToken: cancellationToken),
            (status, group, option, relation) =>
            {
                hierarchyValid &= status.HierarchyValid;
                if (group is null) return status;
                if (!groupMap.TryGetValue(group.Uid, out var current)) groupMap.Add(group.Uid, current = group);
                if (option is not null && optionIds.Add(option.Uid)) current.Options.Add(option);
                if (relation is not null && relationIds.Add(relation.Uid)) snapshot.CategoryRelations.Add(relation);
                return status;
            }, splitOn: "Uid,Uid,Uid");
        if (!hierarchyValid) throw new InvalidOperationException("分类层级缺失或存在循环，无法解析标签适用范围。");
        snapshot.Groups = groupMap.Values.ToList();
        return snapshot;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ProductAttributeGroupDto>> GetDefinitionsAsync(Guid? categoryId, CancellationToken cancellationToken) =>
        (await GetSnapshotAsync(categoryId, cancellationToken)).Groups;

    /// <inheritdoc />
    public async Task<IReadOnlyList<ProductAttributeGroup>> GetGroupsAsync(Guid? categoryId, bool forUpdate, CancellationToken cancellationToken)
    {
        var sql = categoryId.HasValue ? CategoryPathSql + GroupColumnsSql : GroupColumnsSql;
        sql += " WHERE g.is_deleted=FALSE";
        if (categoryId.HasValue)
            sql += " AND EXISTS(SELECT 1 FROM public.product_category_attribute_group r WHERE r.is_deleted=FALSE AND r.attribute_group_id=g.uid AND r.category_id IN (SELECT uid FROM category_path))";
        sql += " ORDER BY g.uid";
        if (forUpdate) sql += " FOR UPDATE OF g";
        return (await context.Session.Connection.QueryAsync<ProductAttributeGroup>(new CommandDefinition(
            sql, new { CategoryId = categoryId }, context.Session.Transaction, cancellationToken: cancellationToken))).AsList();
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ProductAttributeGroup>> LockGroupsForUpdateAsync(IEnumerable<Guid> groupIds, CancellationToken cancellationToken) =>
        LockGroupsAsync(groupIds, " FOR UPDATE OF g", cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<ProductAttributeGroup>> LockGroupsForSelectionAsync(IEnumerable<Guid> groupIds, CancellationToken cancellationToken) =>
        LockGroupsAsync(groupIds, " FOR SHARE OF g", cancellationToken);

    /// <summary>只锁应用层已经定位的组集合；所有锁按 Uid 排序，不在仓储中扩大锁集合。</summary>
    private async Task<IReadOnlyList<ProductAttributeGroup>> LockGroupsAsync(IEnumerable<Guid> groupIds, string lockClause, CancellationToken cancellationToken)
    {
        var ids = groupIds.Distinct().ToArray();
        if (ids.Length == 0) return [];
        return (await context.Session.Connection.QueryAsync<ProductAttributeGroup>(new CommandDefinition(
            GroupColumnsSql + " WHERE g.is_deleted=FALSE AND g.uid=ANY(@Ids) ORDER BY g.uid" + lockClause,
            new { Ids = ids }, context.Session.Transaction, cancellationToken: cancellationToken))).AsList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Guid>> GetGroupIdsForOptionsAsync(IEnumerable<Guid> optionIds, CancellationToken cancellationToken)
    {
        var ids = optionIds.Distinct().ToArray();
        if (ids.Length == 0) return [];
        return (await context.Session.Connection.QueryAsync<Guid>(new CommandDefinition("""
            SELECT DISTINCT o.attribute_group_id FROM public.product_attribute_option o
            WHERE o.uid=ANY(@Ids) ORDER BY o.attribute_group_id
            """, new { Ids = ids }, context.Session.Transaction, cancellationToken: cancellationToken))).AsList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ProductCategoryAttributeGroup>> GetRelationsAsync(IEnumerable<Guid>? groupIds, bool forUpdate, CancellationToken cancellationToken)
    {
        var ids = groupIds?.Distinct().ToArray();
        if (ids is { Length: 0 }) return [];
        var sql = """
            SELECT r.uid AS "Uid",r.version AS "Version",r.is_deleted AS "IsDeleted",
                   r.created_date AS "CreatedDate",r.created_by AS "CreatedBy",
                   r.updated_date AS "UpdatedDate",r.updated_by AS "UpdatedBy",
                   r.category_id AS "CategoryId",r.attribute_group_id AS "AttributeGroupId"
            FROM public.product_category_attribute_group r WHERE r.is_deleted=FALSE
            """;
        if (ids is not null) sql += " AND r.attribute_group_id=ANY(@Ids)";
        sql += " ORDER BY r.uid";
        if (forUpdate) sql += " FOR UPDATE OF r";
        return (await context.Session.Connection.QueryAsync<ProductCategoryAttributeGroup>(new CommandDefinition(
            sql, new { Ids = ids }, context.Session.Transaction, cancellationToken: cancellationToken))).AsList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ProductAttributeOption>> GetOptionsAsync(IEnumerable<Guid> groupIds, bool forUpdate, CancellationToken cancellationToken)
    {
        var ids = groupIds.Distinct().ToArray();
        if (ids.Length == 0) return [];
        var sql = """
            SELECT o.uid AS "Uid",o.version AS "Version",o.is_deleted AS "IsDeleted",
                   o.created_date AS "CreatedDate",o.created_by AS "CreatedBy",
                   o.updated_date AS "UpdatedDate",o.updated_by AS "UpdatedBy",
                   o.attribute_group_id AS "AttributeGroupId",o.option_code AS "OptionCode",
                   o.option_name AS "OptionName",o.sort_order AS "SortOrder"
            FROM public.product_attribute_option o WHERE o.is_deleted=FALSE AND o.attribute_group_id=ANY(@Ids)
            ORDER BY o.uid
            """;
        if (forUpdate) sql += " FOR UPDATE OF o";
        return (await context.Session.Connection.QueryAsync<ProductAttributeOption>(new CommandDefinition(
            sql, new { Ids = ids }, context.Session.Transaction, cancellationToken: cancellationToken))).AsList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ProductMasterAttributeSelection>> GetSelectionsAsync(IEnumerable<Guid> masterIds, CancellationToken cancellationToken)
    {
        var ids = masterIds.Distinct().ToArray();
        if (ids.Length == 0) return [];
        return (await context.Session.Connection.QueryAsync<ProductMasterAttributeSelection>(new CommandDefinition("""
            SELECT s.uid AS "Uid",s.version AS "Version",s.is_deleted AS "IsDeleted",
                   s.product_master_id AS "ProductMasterId",s.option_id AS "OptionId"
            FROM public.product_master_attribute_selection s
            WHERE s.is_deleted=FALSE AND s.product_master_id=ANY(@Ids) ORDER BY s.uid
            """, new { Ids = ids }, context.Session.Transaction, cancellationToken: cancellationToken))).AsList();
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ProductMasterAttributeSelectionDto>> GetSelectionDetailsAsync(IEnumerable<Guid> masterIds, CancellationToken cancellationToken) =>
        ReadSelectionDetailsAsync(masterIds.Distinct().ToArray(), null, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<ProductMasterAttributeSelectionDto>> GetSelectedReferencesAsync(IEnumerable<Guid>? groupIds, CancellationToken cancellationToken) =>
        ReadSelectionDetailsAsync(null, groupIds?.Distinct().ToArray(), cancellationToken);

    /// <summary>选择以主型号真实分类解析祖先关联；失效选项与断链仍返回原身份，避免被解释为清空。</summary>
    private async Task<IReadOnlyList<ProductMasterAttributeSelectionDto>> ReadSelectionDetailsAsync(Guid[]? masterIds, Guid[]? groupIds, CancellationToken cancellationToken)
    {
        if (masterIds is { Length: 0 } || groupIds is { Length: 0 }) return [];
        var masterFilter = masterIds is null ? "" : " AND m.uid=ANY(@MasterIds)";
        var groupFilter = groupIds is null ? "" : " AND o.attribute_group_id=ANY(@GroupIds)";
        var sql = """
            WITH RECURSIVE category_path AS (
                SELECT m.uid AS master_id,c.uid,c.parent_id,ARRAY[c.uid] AS visited
                FROM public.product_master m
                INNER JOIN public.product_category c ON c.uid=m.category_id AND c.is_deleted=FALSE
                WHERE m.is_deleted=FALSE
            """ + masterFilter + "\n" + """
                UNION ALL
                SELECT p.master_id,c.uid,c.parent_id,p.visited||c.uid
                FROM category_path p
                INNER JOIN public.product_category c ON c.uid=p.parent_id AND c.is_deleted=FALSE
                WHERE NOT c.uid=ANY(p.visited)
            )
            SELECT s.product_master_id AS "ProductMasterId",s.option_id AS "OptionId",
                   coalesce(o.attribute_group_id,'00000000-0000-0000-0000-000000000000'::uuid) AS "AttributeGroupId",
                   m.category_id AS "CategoryId",coalesce(g.group_name,'标签组已失效') AS "GroupName",
                   coalesce(o.option_name,'标签值已失效') AS "OptionName",coalesce(g.selection_mode,0) AS "SelectionMode",
                   coalesce(g.sort_order,0) AS "GroupSortOrder",coalesce(o.sort_order,0) AS "OptionSortOrder",
                   (o.uid IS NOT NULL AND o.is_deleted=FALSE AND g.uid IS NOT NULL AND g.is_deleted=FALSE
                    AND EXISTS(SELECT 1 FROM category_path p WHERE p.master_id=m.uid
                        AND (p.parent_id IS NULL OR p.parent_id='00000000-0000-0000-0000-000000000000'::uuid))
                    AND EXISTS(SELECT 1 FROM category_path p
                        INNER JOIN public.product_category_attribute_group r ON r.category_id=p.uid
                            AND r.is_deleted=FALSE AND r.attribute_group_id=g.uid
                        WHERE p.master_id=m.uid)) AS "IsAvailable"
            FROM public.product_master_attribute_selection s
            INNER JOIN public.product_master m ON m.uid=s.product_master_id AND m.is_deleted=FALSE
            LEFT JOIN public.product_attribute_option o ON o.uid=s.option_id
            LEFT JOIN public.product_attribute_group g ON g.uid=o.attribute_group_id
            WHERE s.is_deleted=FALSE
            """ + masterFilter + groupFilter + " ORDER BY g.sort_order,g.uid,o.sort_order,o.uid,s.uid";
        return (await context.Session.Connection.QueryAsync<ProductMasterAttributeSelectionDto>(new CommandDefinition(
            sql, new { MasterIds = masterIds, GroupIds = groupIds }, context.Session.Transaction, cancellationToken: cancellationToken))).AsList();
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
            SELECT EXISTS(SELECT 1 FROM public.product_category_attribute_group
                WHERE is_deleted=FALSE AND category_id=ANY(@Ids))
            """, new { Ids = ids }, context.Session.Transaction, cancellationToken: cancellationToken));
    }

    /// <inheritdoc />
    public Task<RepositoryWriteResult> AddGroupAsync(ProductAttributeGroup entity) => GuardUniqueAsync(() => groups.AddAsync(entity), entity, null, "product_attribute_group", "ux_product_attribute_group_code", "标签组编码已被占用，请刷新后重试。");
    /// <inheritdoc />
    public Task<RepositoryWriteResult> UpdateGroupAsync(ProductAttributeGroup entity) => GuardUniqueAsync(() => groups.UpdateAsync(entity), entity, entity.Version, "product_attribute_group", "ux_product_attribute_group_code", "标签组编码已被占用，请刷新后重试。");
    /// <inheritdoc />
    public Task<RepositoryWriteResult> DeleteGroupAsync(Guid uid, int version) => groups.DeleteAsync(uid, version, true);
    /// <inheritdoc />
    public Task<RepositoryWriteResult> AddOptionAsync(ProductAttributeOption entity) => GuardUniqueAsync(() => options.AddAsync(entity), entity, null, "product_attribute_option", "ux_product_attribute_option_group_code", "标签选项编码已被占用，请刷新后重试。");
    /// <inheritdoc />
    public Task<RepositoryWriteResult> UpdateOptionAsync(ProductAttributeOption entity) => GuardUniqueAsync(() => options.UpdateAsync(entity), entity, entity.Version, "product_attribute_option", "ux_product_attribute_option_group_code", "标签选项编码已被占用，请刷新后重试。");
    /// <inheritdoc />
    public Task<RepositoryWriteResult> DeleteOptionAsync(Guid uid, int version) => options.DeleteAsync(uid, version, true);
    /// <inheritdoc />
    public Task<RepositoryWriteResult> AddRelationAsync(ProductCategoryAttributeGroup entity) => GuardUniqueAsync(() => relations.AddAsync(entity), entity, null, "product_category_attribute_group", "ux_product_category_attribute_group_category_group", "该分类已直接关联此标签组，请刷新后重试。");
    /// <inheritdoc />
    public Task<RepositoryWriteResult> DeleteRelationAsync(Guid uid, int version) => relations.DeleteAsync(uid, version, true);
    /// <inheritdoc />
    public Task<RepositoryWriteResult> AddSelectionAsync(ProductMasterAttributeSelection entity) => selections.AddAsync(entity);
    /// <inheritdoc />
    public Task<RepositoryWriteResult> DeleteSelectionAsync(Guid uid, int version) => selections.DeleteAsync(uid, version, true);

    /// <summary>仅分类指定业务唯一索引，不把主键或其他约束异常误报为编码或关联重复。</summary>
    private static async Task<RepositoryWriteResult> GuardUniqueAsync(Func<Task<RepositoryWriteResult>> write, BaseEntity entity,
        int? oldVersion, string table, string index, string message)
    {
        try { return await write(); }
        catch (PostgresException exception) when (exception.SqlState == "23505" && exception.SchemaName == "public"
            && exception.TableName == table && exception.ConstraintName == index)
        { return RepositoryWriteResult.Fail(entity.Uid, oldVersion, ServiceErrorCodes.Validation, message); }
    }

    /// <summary>联表空快照也携带路径完整性，防止分类异常被误判为没有定义。</summary>
    private sealed class SnapshotStatus
    {
        /// <summary>分类路径是否到达有效根；全局查询始终为真。</summary>
        public bool HierarchyValid { get; set; }
    }
}
