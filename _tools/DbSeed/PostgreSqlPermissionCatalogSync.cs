using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Npgsql;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Core.Entities.Auth;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Tools.DbSeed;

/// <summary>在既有PG会话中同步代码权限目录；仅负责同步/预览，不引导管理员或接管工具入口。</summary>
public sealed class PostgreSqlPermissionCatalogSync(DbSession session)
{
    private const string SchemaError = "目标库缺少当前权限定义或角色权限关系结构；目录同步不迁移结构。";
    private static readonly EntityMetadata Definitions = EntityMetadataCache.GetMetadata(typeof(SysPermission));
    private static readonly EntityMetadata Relations = EntityMetadataCache.GetMetadata(typeof(SysRolePermission));
    /// <summary>承接原全量目录读取的五字段，不把状态或关系加入展示同步计划。</summary>
    private sealed record ExistingPermission(Guid Uid, string Key, string Name, string Group, int SortOrder);
    /// <summary>保持代码目录顺序与原新增/更新计划，既有身份用于精确三列更新。</summary>
    private sealed record PlannedPermission(PermissionDefinition Definition, int SortOrder, ExistingPermission? Existing);

    /// <summary>自有Serializable事务先排角色/定义，再同步全目录；dry-run同锁同门禁但不写，异常不重试。</summary>
    public async Task RunAsync(bool dryRun, CancellationToken cancellationToken = default)
    {
        if (session.Transaction is not null) throw new InvalidOperationException("目录同步不能接管已有事务。");
        var definitions = PermissionCatalog.All;
        if (definitions.Select(item => item.PermissionKey).Distinct(StringComparer.OrdinalIgnoreCase).Count() != definitions.Count)
            throw new InvalidOperationException("代码权限目录包含重复的权限键。");
        cancellationToken.ThrowIfCancellationRequested();
        var transaction = (DbTransaction)session.Connection.BeginTransaction(IsolationLevel.Serializable);
        session.Transaction = transaction;
        var failed = false;
        try
        {
            await LockAsync(EntityMetadataCache.GetMetadata(typeof(SysRole)), cancellationToken);
            await LockAsync(Definitions, cancellationToken);
            await VerifySchemaAsync(cancellationToken);
            var existing = await ReadExistingAsync(cancellationToken);
            var codeKeys = definitions.Select(item => item.PermissionKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var external = existing.Values.Where(item => !codeKeys.Contains(item.Key)).OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase).ToList();
            var insert = new List<PlannedPermission>(); var update = new List<PlannedPermission>();
            for (var index = 0; index < definitions.Count; index++)
            {
                var definition = definitions[index]; var order = index + 1;
                if (existing.TryGetValue(definition.PermissionKey, out var spelling)
                    && !string.Equals(spelling.Key, definition.PermissionKey, StringComparison.Ordinal))
                    throw new InvalidOperationException($"数据库权限键大小写与代码不一致：{spelling.Key}，应为 {definition.PermissionKey}；请显式迁移，同步不会改写权限键。");
                if (!existing.TryGetValue(definition.PermissionKey, out var current)) insert.Add(new(definition, order, null));
                else if (!string.Equals(current.Name, definition.DisplayName, StringComparison.Ordinal)
                    || !string.Equals(current.Group, definition.GroupKey, StringComparison.Ordinal) || current.SortOrder != order)
                    update.Add(new(definition, order, current));
            }
            Console.WriteLine($"目录同步{(dryRun ? "预览" : "计划")}：新增 {insert.Count}，更新 {update.Count}，目录外 {external.Count}。");
            foreach (var item in insert) Console.WriteLine($"新增：{item.Definition.PermissionKey}");
            foreach (var item in update) Console.WriteLine($"更新：{item.Definition.PermissionKey}（名称/分组/排序）");
            foreach (var item in external) Console.WriteLine($"目录外保留：{item.Key}");
            if (!dryRun)
            {
                foreach (var item in insert) await InsertAsync(item, cancellationToken);
                foreach (var item in update) await UpdateAsync(item, cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
            Console.WriteLine(dryRun ? "预览完成；没有写入数据库。"
                : $"同步完成：新增 {insert.Count}，更新 {update.Count}；目录外 {external.Count} 项保留。");
        }
        catch
        {
            failed = true;
            try { await transaction.RollbackAsync(CancellationToken.None); }
            catch { /* 清理失败不遮蔽原错误或未知提交，不自动重试。 */ }
            throw;
        }
        finally
        {
            try { await transaction.DisposeAsync(); }
            catch
            {
                try { session.Connection.Dispose(); } catch { /* 不能用关闭异常覆盖本层原异常。 */ }
                if (!failed) throw;
            }
            finally { session.Transaction = null; }
        }
    }

    /// <summary>首批纯LOCK在任何快照读取前取得；仅缺表转换成原结构诊断，保留数据库异常为InnerException。</summary>
    private async Task LockAsync(EntityMetadata map, CancellationToken token)
    {
        try { await session.Connection.ExecuteAsync(Command($"LOCK TABLE {Table(map)} IN SHARE ROW EXCLUSIVE MODE;", null, token)); }
        catch (PostgresException error) when (error.SqlState == PostgresErrorCodes.UndefinedTable)
        { throw new InvalidOperationException(SchemaError, error); }
    }

    /// <summary>只校验原同步所需定义/RP结构，不隐式迁移；在全部首锁后才建立首快照。</summary>
    private async Task VerifySchemaAsync(CancellationToken token)
    {
        var properties = new[] { nameof(SysPermission.Uid), nameof(SysPermission.PermissionKey), nameof(SysPermission.DisplayName),
            nameof(SysPermission.GroupKey), nameof(SysPermission.SortOrder), nameof(SysPermission.IsActive), nameof(SysPermission.IsDeleted) };
        var columns = string.Join(",", properties.Select(property => Literal(Definitions.GetProperty(property).ColumnName)));
        var parameters = new DynamicParameters();
        parameters.Add("Definitions", Table(Definitions), DbType.String); parameters.Add("Relations", Table(Relations), DbType.String);
        parameters.Add("Schema", Definitions.SchemaName, DbType.String); parameters.Add("DefinitionName", Definitions.TableName, DbType.String);
        parameters.Add("RelationName", Relations.TableName, DbType.String);
        parameters.Add("PermissionId", Relations.GetProperty(nameof(SysRolePermission.PermissionId)).ColumnName, DbType.String);
        var valid = await session.Connection.ExecuteScalarAsync<bool>(Command($"""
            SELECT to_regclass(@Definitions) IS NOT NULL AND to_regclass(@Relations) IS NOT NULL
              AND (SELECT count(*) FROM information_schema.columns WHERE table_schema=@Schema AND table_name=@DefinitionName
                AND column_name IN ({columns}))={properties.Length}
              AND EXISTS(SELECT 1 FROM information_schema.columns WHERE table_schema=@Schema
                AND table_name=@RelationName AND column_name=@PermissionId);
            """, parameters, token));
        if (!valid) throw new InvalidOperationException(SchemaError);
    }

    /// <summary>全量读取包含停用/删除/目录外；typed读取保留非空字段读取失败，重复键按原IgnoreCase拒绝。</summary>
    private async Task<Dictionary<string, ExistingPermission>> ReadExistingAsync(CancellationToken token)
    {
        var columns = new[] { nameof(SysPermission.Uid), nameof(SysPermission.PermissionKey), nameof(SysPermission.DisplayName),
            nameof(SysPermission.GroupKey), nameof(SysPermission.SortOrder) };
        var result = new Dictionary<string, ExistingPermission>(StringComparer.OrdinalIgnoreCase);
        await using var reader = (DbDataReader)await session.Connection.ExecuteReaderAsync(Command(
            $"SELECT {string.Join(",", columns.Select(Column))} FROM {Table(Definitions)} FOR UPDATE;", null, token));
        while (await reader.ReadAsync(token))
        {
            var item = new ExistingPermission(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetInt32(4));
            if (!result.TryAdd(item.Key, item)) throw new InvalidOperationException($"数据库权限定义存在重复键：{item.Key}。");
        }
        return result;
    }

    /// <summary>缺键只建定义，保持Version1/活动/未删/UTC审计，不写角色授权或账号。</summary>
    private Task<int> InsertAsync(PlannedPermission item, CancellationToken token)
    {
        var parameters = BindDefinition(item); parameters.Add("Uid", Guid.NewGuid(), DbType.Guid);
        return RequireOneAsync($"""
            INSERT INTO {Table(Definitions)}
              ({Column(nameof(SysPermission.Uid))},{Column(nameof(SysPermission.Version))},{Column(nameof(SysPermission.IsDeleted))},
               {Column(nameof(SysPermission.CreatedDate))},{Column(nameof(SysPermission.CreatedBy))},{Column(nameof(SysPermission.UpdatedDate))},{Column(nameof(SysPermission.UpdatedBy))},
               {Column(nameof(SysPermission.PermissionKey))},{Column(nameof(SysPermission.DisplayName))},{Column(nameof(SysPermission.GroupKey))},{Column(nameof(SysPermission.SortOrder))},{Column(nameof(SysPermission.IsActive))})
            VALUES (@Uid,1,FALSE,clock_timestamp() AT TIME ZONE 'UTC','DbSeed',clock_timestamp() AT TIME ZONE 'UTC','DbSeed',
                @PermissionKey,@DisplayName,@GroupKey,@SortOrder,TRUE);
            """, parameters, $"插入权限定义失败：{item.Definition.PermissionKey}。", token);
    }

    /// <summary>已有定义严格只改三展示列，不改Version/审计/Key/状态/UID或关系。</summary>
    private Task<int> UpdateAsync(PlannedPermission item, CancellationToken token)
    {
        var parameters = BindDefinition(item); parameters.Add("Uid", item.Existing!.Uid, DbType.Guid);
        return RequireOneAsync($"""
            UPDATE {Table(Definitions)} SET {Column(nameof(SysPermission.DisplayName))}=@DisplayName,
                {Column(nameof(SysPermission.GroupKey))}=@GroupKey,{Column(nameof(SysPermission.SortOrder))}=@SortOrder
            WHERE {Column(nameof(SysPermission.Uid))}=@Uid AND {Column(nameof(SysPermission.PermissionKey))}=@PermissionKey;
            """, parameters, $"更新权限定义失败：{item.Definition.PermissionKey}。", token);
    }

    /// <summary>必须精确影响一行，否则让原同步整体回滚，不吞唯一/序列化错误。</summary>
    private async Task<int> RequireOneAsync(string sql, DynamicParameters parameters, string message, CancellationToken token)
    {
        var count = await session.Connection.ExecuteAsync(Command(sql, parameters, token));
        if (count != 1) throw new InvalidOperationException(message);
        return count;
    }

    /// <summary>工具固定字段按text/integer明确绑定，NULL文本不让驱动猜测；不复制公共参数框架。</summary>
    private static DynamicParameters BindDefinition(PlannedPermission item)
    {
        var parameters = new DynamicParameters();
        parameters.Add("PermissionKey", item.Definition.PermissionKey, DbType.String);
        parameters.Add("DisplayName", item.Definition.DisplayName, DbType.String);
        parameters.Add("GroupKey", item.Definition.GroupKey, DbType.String);
        parameters.Add("SortOrder", item.SortOrder, DbType.Int32);
        return parameters;
    }

    /// <summary>借用自有事务和调用方令牌，每条SQL边界一致。</summary>
    private CommandDefinition Command(string sql, object? parameters, CancellationToken token) => new(sql, parameters, session.Transaction, cancellationToken: token);
    /// <summary>只引用固定元数据标识符。</summary>
    private static string Table(EntityMetadata map) => $"{PostgreSqlNames.Quote(map.SchemaName)}.{PostgreSqlNames.Quote(map.TableName)}";
    /// <summary>只投影定义实体的固定物理列。</summary>
    private static string Column(string property) => PostgreSqlNames.Quote(Definitions.GetProperty(property).ColumnName);
    /// <summary>schema检查的固定元数据列作为SQL常量，业务输入仍全部参数化。</summary>
    private static string Literal(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
}

