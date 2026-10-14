using Dapper;
using Npgsql;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Products.Attributes;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Entities.Systems.Dictionaries.Data;
using SwiftExport.Core.Interfaces;
using SwiftExport.Core.Interfaces.Products.Attributes;
using SwiftExport.Infrastructure.Contexts;

namespace SwiftExport.Infrastructure.Repositories.Products.Attributes;

/// <summary>模板标签四表的 PostgreSQL 读写；基础写入委托同一会话的通用审计仓储。</summary>
public sealed class PostgreSqlProductAttributeRepository(RepositoryBaseContext context,
    IRepository<ProductAttributeGroup> groups, IRepository<ProductAttributeOption> options,
    IRepository<ProductAttributeSelection> selections,
    IRepository<ProductAttributeProfileGroup> relations) : IProductAttributeRepository
{
    private const string GroupColumnsSql = """
        SELECT g.uid AS "Uid",g.version AS "Version",g.is_deleted AS "IsDeleted",
               g.created_date AS "CreatedDate",g.created_by AS "CreatedBy",
               g.updated_date AS "UpdatedDate",g.updated_by AS "UpdatedBy",
               g.group_code AS "GroupCode",g.group_name AS "GroupName",
               g.selection_mode AS "SelectionMode",g.sort_order AS "SortOrder"
        FROM public.product_attribute_group g
        """;

    // 归属由固定四种业务对象解析；不以 UUID 单独判定对象，也不动态拼接表名。
    // 对象有效性与模板有效性分开，关联断链时仍保留已有选择供维护校验和明确提示。
    private const string OwnersSql = """
        WITH attribute_owners AS (
            SELECT 1 AS owner_type,m.uid AS owner_id,m.product_attribute_profile_id
            FROM public.product_master m WHERE m.is_deleted=FALSE
            UNION ALL
            SELECT 2,cp.uid,m.product_attribute_profile_id
            FROM public.customer_product cp
            LEFT JOIN public.product p ON p.uid=cp.sku_id AND p.is_deleted=FALSE
            LEFT JOIN public.product_master m ON m.uid=p.product_master_id AND m.is_deleted=FALSE
            WHERE cp.is_deleted=FALSE
            UNION ALL
            SELECT 3,pn.uid,m.product_attribute_profile_id
            FROM public.product_neutral pn
            LEFT JOIN public.product_master m ON m.uid=pn.product_master_id AND m.is_deleted=FALSE
            WHERE pn.is_deleted=FALSE
            UNION ALL
            SELECT 4,p.uid,m.product_attribute_profile_id
            FROM public.product p
            LEFT JOIN public.product_master m ON m.uid=p.product_master_id AND m.is_deleted=FALSE
            WHERE p.is_deleted=FALSE
        )
        """;

    /// <inheritdoc />
    public async Task<ProductAttributeDefinitionsDto> GetSnapshotAsync(Guid? profileId, CancellationToken cancellationToken)
    {
        var snapshot = new ProductAttributeDefinitionsDto { ProfileId = profileId ?? Guid.Empty };
        var groupMap = new Dictionary<Guid, ProductAttributeGroupDto>();
        var optionIds = new HashSet<Guid>();
        var relationIds = new HashSet<Guid>();
        // 一条联表语句提供同一提交快照；无模板关联的全局组保留，联接重复身份在此去重。
        await context.Session.Connection.QueryAsync<SnapshotAnchor, ProductAttributeGroupDto, ProductAttributeOptionDto,
            ProductAttributeProfileGroupDto, SnapshotAnchor>(new CommandDefinition("""
            WITH eligible_relations AS (
                SELECT r.uid,r.version,r.product_attribute_profile_id,r.attribute_group_id
                FROM public.product_attribute_profile_group r
                INNER JOIN public.sys_dict_data d ON d.uid=r.product_attribute_profile_id
                    AND d.is_deleted=FALSE AND d.is_enabled=TRUE
                    AND d.dict_code COLLATE public.erp_dictionary_ci=@ProfileDictCode COLLATE public.erp_dictionary_ci
                WHERE r.is_deleted=FALSE
                  AND (@ProfileId::uuid IS NULL OR r.product_attribute_profile_id=@ProfileId::uuid)
            ), eligible_groups AS (
                SELECT g.uid,g.version,g.group_code,g.group_name,g.selection_mode,g.sort_order
                FROM public.product_attribute_group g
                WHERE g.is_deleted=FALSE AND (@ProfileId::uuid IS NULL
                    OR g.uid IN (SELECT attribute_group_id FROM eligible_relations))
            )
            SELECT 1 AS "Anchor",
                   g.uid AS "Uid",g.version AS "Version",g.group_code AS "GroupCode",g.group_name AS "GroupName",
                   g.selection_mode AS "SelectionMode",g.sort_order AS "SortOrder",
                   o.uid AS "Uid",o.version AS "Version",o.attribute_group_id AS "AttributeGroupId",
                   o.option_code AS "OptionCode",o.option_name AS "OptionName",o.sort_order AS "SortOrder",
                   r.uid AS "Uid",r.version AS "Version",r.product_attribute_profile_id AS "ProductAttributeProfileId",
                   r.attribute_group_id AS "AttributeGroupId"
            FROM (SELECT 1) anchor
            LEFT JOIN eligible_groups g ON TRUE
            LEFT JOIN public.product_attribute_option o ON o.attribute_group_id=g.uid AND o.is_deleted=FALSE
            LEFT JOIN eligible_relations r ON r.attribute_group_id=g.uid
            ORDER BY g.sort_order,g.uid,o.sort_order,o.uid,r.product_attribute_profile_id,r.uid
            """, new { ProfileId = profileId, ProfileDictCode = DictCodes.ProductAttributeProfile }, context.Session.Transaction,
                cancellationToken: cancellationToken),
            (anchor, group, option, relation) =>
            {
                if (group is null) return anchor;
                if (!groupMap.TryGetValue(group.Uid, out var current)) groupMap.Add(group.Uid, current = group);
                if (option is not null && optionIds.Add(option.Uid)) current.Options.Add(option);
                if (relation is not null && relationIds.Add(relation.Uid)) snapshot.ProfileRelations.Add(relation);
                return anchor;
            }, splitOn: "Uid,Uid,Uid");
        snapshot.Groups = groupMap.Values.ToList();
        return snapshot;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ProductAttributeGroupDto>> GetDefinitionsAsync(Guid? profileId, CancellationToken cancellationToken) =>
        (await GetSnapshotAsync(profileId, cancellationToken)).Groups;

    /// <inheritdoc />
    public async Task<IReadOnlyList<ProductAttributeGroup>> GetGroupsAsync(Guid? profileId, bool forUpdate, CancellationToken cancellationToken)
    {
        var sql = GroupColumnsSql + " WHERE g.is_deleted=FALSE";
        if (profileId.HasValue)
            sql += " AND EXISTS(SELECT 1 FROM public.product_attribute_profile_group r WHERE r.is_deleted=FALSE AND r.attribute_group_id=g.uid AND r.product_attribute_profile_id=@ProfileId)";
        sql += " ORDER BY g.uid";
        if (forUpdate) sql += " FOR UPDATE OF g";
        return (await context.Session.Connection.QueryAsync<ProductAttributeGroup>(new CommandDefinition(
            sql, new { ProfileId = profileId }, context.Session.Transaction, cancellationToken: cancellationToken))).AsList();
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ProductAttributeGroup>> LockGroupsForUpdateAsync(IEnumerable<Guid> groupIds, CancellationToken cancellationToken) =>
        LockGroupsAsync(groupIds, " FOR UPDATE OF g", cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<ProductAttributeGroup>> LockGroupsForSelectionAsync(IEnumerable<Guid> groupIds, CancellationToken cancellationToken) =>
        LockGroupsAsync(groupIds, " FOR SHARE OF g", cancellationToken);

    /// <summary>只锁应用层定位的组集合；锁按 Uid 排序，不扩大锁范围。</summary>
    private async Task<IReadOnlyList<ProductAttributeGroup>> LockGroupsAsync(IEnumerable<Guid> groupIds, string lockClause, CancellationToken cancellationToken)
    {
        var ids = groupIds.Distinct().ToArray();
        if (ids.Length == 0) return [];
        if (context.Session.Transaction is null) throw new InvalidOperationException("标签组锁定需要调用方事务。");
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
    public async Task<IReadOnlyList<ProductAttributeProfileGroup>> GetRelationsAsync(IEnumerable<Guid>? groupIds, bool forUpdate, CancellationToken cancellationToken)
    {
        var ids = groupIds?.Distinct().ToArray();
        if (ids is { Length: 0 }) return [];
        var sql = """
            SELECT r.uid AS "Uid",r.version AS "Version",r.is_deleted AS "IsDeleted",
                   r.created_date AS "CreatedDate",r.created_by AS "CreatedBy",
                   r.updated_date AS "UpdatedDate",r.updated_by AS "UpdatedBy",
                   r.product_attribute_profile_id AS "ProductAttributeProfileId",r.attribute_group_id AS "AttributeGroupId"
            FROM public.product_attribute_profile_group r WHERE r.is_deleted=FALSE
            """;
        if (ids is not null) sql += " AND r.attribute_group_id=ANY(@Ids)";
        sql += " ORDER BY r.uid";
        if (forUpdate) sql += " FOR UPDATE OF r";
        return (await context.Session.Connection.QueryAsync<ProductAttributeProfileGroup>(new CommandDefinition(
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
    public async Task<IReadOnlyList<ProductAttributeSelection>> GetSelectionsAsync(ProductAttributeOwnerType ownerType,
        IEnumerable<Guid> ownerIds, CancellationToken cancellationToken)
    {
        var ids = ownerIds.Distinct().ToArray();
        if (ids.Length == 0) return [];
        return (await context.Session.Connection.QueryAsync<ProductAttributeSelection>(new CommandDefinition("""
            SELECT s.uid AS "Uid",s.version AS "Version",s.is_deleted AS "IsDeleted",
                   s.created_date AS "CreatedDate",s.created_by AS "CreatedBy",
                   s.updated_date AS "UpdatedDate",s.updated_by AS "UpdatedBy",
                   s.owner_type AS "OwnerType",s.owner_id AS "OwnerId",s.option_id AS "OptionId"
            FROM public.product_attribute_selection s
            WHERE s.is_deleted=FALSE AND s.owner_type=@OwnerType AND s.owner_id=ANY(@Ids) ORDER BY s.uid
            """, new { OwnerType = ownerType, Ids = ids }, context.Session.Transaction, cancellationToken: cancellationToken))).AsList();
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ProductAttributeSelectionDto>> GetSelectionDetailsAsync(ProductAttributeOwnerType ownerType,
        IEnumerable<Guid> ownerIds, CancellationToken cancellationToken) =>
        ReadSelectionDetailsAsync(ownerType, ownerIds.Distinct().ToArray(), null, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<ProductAttributeSelectionDto>> GetSelectedReferencesAsync(IEnumerable<Guid>? groupIds, CancellationToken cancellationToken) =>
        ReadSelectionDetailsAsync(null, null, groupIds?.Distinct().ToArray(), cancellationToken);

    /// <summary>从固定对象关系取得当前模板；失效定义或关联断链仍返回已有选择的原身份。</summary>
    private async Task<IReadOnlyList<ProductAttributeSelectionDto>> ReadSelectionDetailsAsync(ProductAttributeOwnerType? ownerType,
        Guid[]? ownerIds, Guid[]? groupIds, CancellationToken cancellationToken)
    {
        if (ownerIds is { Length: 0 } || groupIds is { Length: 0 }) return [];
        var ownerFilter = ownerType.HasValue ? " AND s.owner_type=@OwnerType" : "";
        if (ownerIds is not null) ownerFilter += " AND s.owner_id=ANY(@OwnerIds)";
        var groupFilter = groupIds is null ? "" : " AND o.attribute_group_id=ANY(@GroupIds)";
        var sql = OwnersSql + """
            SELECT s.owner_type AS "OwnerType",s.owner_id AS "OwnerId",s.option_id AS "OptionId",
                   coalesce(o.attribute_group_id,'00000000-0000-0000-0000-000000000000'::uuid) AS "AttributeGroupId",
                   a.product_attribute_profile_id AS "ProductAttributeProfileId",coalesce(g.group_name,'标签组已失效') AS "GroupName",
                   coalesce(o.option_name,'标签值已失效') AS "OptionName",coalesce(g.selection_mode,0) AS "SelectionMode",
                   coalesce(g.sort_order,0) AS "GroupSortOrder",coalesce(o.sort_order,0) AS "OptionSortOrder",
                   (o.uid IS NOT NULL AND o.is_deleted=FALSE AND g.uid IS NOT NULL AND g.is_deleted=FALSE
                    AND d.uid IS NOT NULL AND d.is_deleted=FALSE AND d.is_enabled=TRUE
                    AND d.dict_code COLLATE public.erp_dictionary_ci=@ProfileDictCode COLLATE public.erp_dictionary_ci
                    AND EXISTS(SELECT 1 FROM public.product_attribute_profile_group r
                        WHERE r.product_attribute_profile_id=a.product_attribute_profile_id AND r.attribute_group_id=g.uid
                            AND r.is_deleted=FALSE)) AS "IsAvailable"
            FROM public.product_attribute_selection s
            INNER JOIN attribute_owners a ON a.owner_type=s.owner_type AND a.owner_id=s.owner_id
            LEFT JOIN public.product_attribute_option o ON o.uid=s.option_id
            LEFT JOIN public.product_attribute_group g ON g.uid=o.attribute_group_id
            LEFT JOIN public.sys_dict_data d ON d.uid=a.product_attribute_profile_id
            WHERE s.is_deleted=FALSE
            """ + ownerFilter + groupFilter + " ORDER BY g.sort_order,g.uid,o.sort_order,o.uid,s.uid";
        return (await context.Session.Connection.QueryAsync<ProductAttributeSelectionDto>(new CommandDefinition(
            sql, new { OwnerType = ownerType, OwnerIds = ownerIds, GroupIds = groupIds, ProfileDictCode = DictCodes.ProductAttributeProfile },
            context.Session.Transaction, cancellationToken: cancellationToken))).AsList();
    }

    /// <inheritdoc />
    public Task<int> CountReferencedOwnersAsync(IEnumerable<Guid> optionIds, CancellationToken cancellationToken)
    {
        var ids = optionIds.Distinct().ToArray();
        if (ids.Length == 0) return Task.FromResult(0);
        return context.Session.Connection.ExecuteScalarAsync<int>(new CommandDefinition(OwnersSql + """
            SELECT count(DISTINCT (s.owner_type,s.owner_id))
            FROM public.product_attribute_selection s
            INNER JOIN attribute_owners a ON a.owner_type=s.owner_type AND a.owner_id=s.owner_id
            WHERE s.is_deleted=FALSE AND s.option_id=ANY(@Ids)
            """, new { Ids = ids }, context.Session.Transaction, cancellationToken: cancellationToken));
    }

    /// <inheritdoc />
    public Task<int> CountMultipleSelectionsAsync(Guid groupId, CancellationToken cancellationToken) =>
        context.Session.Connection.ExecuteScalarAsync<int>(new CommandDefinition(OwnersSql + """
            SELECT count(*) FROM (
                SELECT s.owner_type,s.owner_id
                FROM public.product_attribute_selection s
                INNER JOIN attribute_owners a ON a.owner_type=s.owner_type AND a.owner_id=s.owner_id
                INNER JOIN public.product_attribute_option o ON o.uid=s.option_id AND o.is_deleted=FALSE
                WHERE s.is_deleted=FALSE AND o.attribute_group_id=@GroupId
                GROUP BY s.owner_type,s.owner_id HAVING count(*)>1
            ) multiple_choices
            """, new { GroupId = groupId }, context.Session.Transaction, cancellationToken: cancellationToken));

    /// <inheritdoc />
    public async Task<IReadOnlyList<SysDictData>> GetProfilesAsync(IEnumerable<Guid> profileIds, bool withShareLock, CancellationToken cancellationToken)
    {
        var ids = profileIds.Distinct().ToArray();
        if (ids.Length == 0) return [];
        if (withShareLock && context.Session.Transaction is null) throw new InvalidOperationException("属性模板共享锁需要调用方事务。");
        var sql = """
            SELECT d.uid AS "Uid",d.version AS "Version",d.is_deleted AS "IsDeleted",
                   d.created_date AS "CreatedDate",d.created_by AS "CreatedBy",d.updated_date AS "UpdatedDate",d.updated_by AS "UpdatedBy",
                   d.dict_code AS "DictCode",d.data_name_en AS "DataNameEn",d.data_name_zh AS "DataNameZh",d.remark AS "Remark",
                   d.sort_order AS "SortOrder",d.css_class AS "CssClass",d.is_system AS "IsSystem",d.is_enabled AS "IsEnabled"
            FROM public.sys_dict_data d WHERE d.uid=ANY(@Ids) ORDER BY d.uid
            """;
        if (withShareLock) sql += " FOR SHARE OF d";
        return (await context.Session.Connection.QueryAsync<SysDictData>(new CommandDefinition(
            sql, new { Ids = ids }, context.Session.Transaction, cancellationToken: cancellationToken))).AsList();
    }

    /// <inheritdoc />
    public Task<int> CountProfileReferencesAsync(Guid profileId, CancellationToken cancellationToken) =>
        context.Session.Connection.ExecuteScalarAsync<int>(new CommandDefinition("""
            SELECT (SELECT count(*) FROM public.product_master m
                        WHERE m.is_deleted=FALSE AND m.product_attribute_profile_id=@ProfileId)
                 + (SELECT count(*) FROM public.product_attribute_profile_group r
                        INNER JOIN public.product_attribute_group g ON g.uid=r.attribute_group_id AND g.is_deleted=FALSE
                        WHERE r.is_deleted=FALSE AND r.product_attribute_profile_id=@ProfileId)
            """, new { ProfileId = profileId }, context.Session.Transaction, cancellationToken: cancellationToken));

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
    public Task<RepositoryWriteResult> AddRelationAsync(ProductAttributeProfileGroup entity) => GuardUniqueAsync(() => relations.AddAsync(entity), entity, null, "product_attribute_profile_group", "ux_product_attribute_profile_group_profile_group", "该模板已关联此标签组，请刷新后重试。");
    /// <inheritdoc />
    public Task<RepositoryWriteResult> DeleteRelationAsync(Guid uid, int version) => relations.DeleteAsync(uid, version, true);
    /// <inheritdoc />
    public Task<RepositoryWriteResult> AddSelectionAsync(ProductAttributeSelection entity) => selections.AddAsync(entity);
    /// <inheritdoc />
    public Task<RepositoryWriteResult> DeleteSelectionAsync(Guid uid, int version) => selections.DeleteAsync(uid, version, true);

    /// <summary>仅识别指定业务唯一索引，不把主键或其他约束异常误报为编码或关联重复。</summary>
    private static async Task<RepositoryWriteResult> GuardUniqueAsync(Func<Task<RepositoryWriteResult>> write, BaseEntity entity,
        int? oldVersion, string table, string index, string message)
    {
        try { return await write(); }
        catch (PostgresException exception) when (exception.SqlState == "23505" && exception.SchemaName == "public"
            && exception.TableName == table && exception.ConstraintName == index)
        { return RepositoryWriteResult.Fail(entity.Uid, oldVersion, ServiceErrorCodes.Validation, message); }
    }

    /// <summary>联表空快照的固定锚点，使空组和空选项由 Dapper 正确映射为空。</summary>
    private sealed class SnapshotAnchor
    {
        /// <summary>固定查询锚点；不参与业务判断。</summary>
        public int Anchor { get; set; }
    }
}
