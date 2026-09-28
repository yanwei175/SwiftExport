using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using SwiftExport.Contracts.DTOs.Auth;

/// <summary>只同步代码权限目录的可展示字段；现有权限身份、状态和授权关系原样保留。</summary>
internal static class PermissionCatalogSync
{
    private sealed record ExistingPermission(Guid Uid, string Key, string Name, string Group,
        int SortOrder);
    private sealed record PlannedPermission(PermissionDefinition Definition, int SortOrder,
        ExistingPermission? Existing);

    /// <summary>在单个串行化事务中对比并同步目录；dry-run 只读取和报告差异。</summary>
    public static async Task RunAsync(string connectionString, bool dryRun)
    {
        var definitions = PermissionCatalog.All;
        if (definitions.Select(item => item.PermissionKey)
            .Distinct(StringComparer.OrdinalIgnoreCase).Count() != definitions.Count)
            throw new InvalidOperationException("代码权限目录包含重复的权限键。");

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable);
        try
        {
            await VerifySchemaAsync(connection, transaction);
            var existing = await ReadExistingAsync(connection, transaction);
            var codeKeys = definitions.Select(item => item.PermissionKey)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var external = existing.Values.Where(item => !codeKeys.Contains(item.Key))
                .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase).ToList();
            var insert = new List<PlannedPermission>();
            var update = new List<PlannedPermission>();
            for (var index = 0; index < definitions.Count; index++)
            {
                var definition = definitions[index];
                var order = index + 1;
                // 运行时权限按精确键匹配，不能把数据库中的大小写漂移当成已同步。
                if (existing.TryGetValue(definition.PermissionKey, out var spelling)
                    && !string.Equals(spelling.Key, definition.PermissionKey, StringComparison.Ordinal))
                    throw new InvalidOperationException($"数据库权限键大小写与代码不一致：{spelling.Key}，应为 {definition.PermissionKey}；请显式迁移，同步不会改写权限键。");
                if (!existing.TryGetValue(definition.PermissionKey, out var current))
                    insert.Add(new PlannedPermission(definition, order, null));
                else if (!string.Equals(current.Name, definition.DisplayName, StringComparison.Ordinal)
                    || !string.Equals(current.Group, definition.GroupKey, StringComparison.Ordinal)
                    || current.SortOrder != order)
                    update.Add(new PlannedPermission(definition, order, current));
            }

            Console.WriteLine($"目录同步{(dryRun ? "预览" : "计划")}：新增 {insert.Count}，更新 {update.Count}，目录外 {external.Count}。");
            foreach (var item in insert) Console.WriteLine($"新增：{item.Definition.PermissionKey}");
            foreach (var item in update)
                Console.WriteLine($"更新：{item.Definition.PermissionKey}（名称/分组/排序）");
            foreach (var item in external) Console.WriteLine($"目录外保留：{item.Key}");
            if (!dryRun)
            {
                foreach (var item in insert) await InsertAsync(connection, transaction, item);
                foreach (var item in update) await UpdateAsync(connection, transaction, item);
            }
            await transaction.CommitAsync();
            Console.WriteLine(dryRun
                ? "预览完成；没有写入数据库。"
                : $"同步完成：新增 {insert.Count}，更新 {update.Count}；目录外 {external.Count} 项保留。");
        }
        catch
        {
            try { await transaction.RollbackAsync(); }
            catch { /* 连接释放后由 SQL Server 清理事务；保留原始异常。 */ }
            throw;
        }
    }

    /// <summary>明确拒绝旧结构，绝不在目录同步模式中执行结构迁移。</summary>
    private static async Task VerifySchemaAsync(SqlConnection connection, SqlTransaction transaction)
    {
        const string sql = """
            SELECT CAST(CASE WHEN OBJECT_ID(N'dbo.SysPermissionDefinitions',N'U') IS NOT NULL
              AND OBJECT_ID(N'dbo.SysRole_Permissions',N'U') IS NOT NULL
              AND COL_LENGTH(N'dbo.SysPermissionDefinitions',N'Uid') IS NOT NULL
              AND COL_LENGTH(N'dbo.SysPermissionDefinitions',N'PermissionKey') IS NOT NULL
              AND COL_LENGTH(N'dbo.SysPermissionDefinitions',N'DisplayName') IS NOT NULL
              AND COL_LENGTH(N'dbo.SysPermissionDefinitions',N'GroupKey') IS NOT NULL
              AND COL_LENGTH(N'dbo.SysPermissionDefinitions',N'SortOrder') IS NOT NULL
              AND COL_LENGTH(N'dbo.SysPermissionDefinitions',N'IsActive') IS NOT NULL
              AND COL_LENGTH(N'dbo.SysPermissionDefinitions',N'IsDeleted') IS NOT NULL
              AND COL_LENGTH(N'dbo.SysRole_Permissions',N'PermissionId') IS NOT NULL
              THEN 1 ELSE 0 END AS bit);
            """;
        await using var command = new SqlCommand(sql, connection, transaction);
        if (!(bool)(await command.ExecuteScalarAsync() ?? false))
            throw new InvalidOperationException("目标库缺少当前权限定义或角色权限关系结构；目录同步不迁移结构。");
    }

    /// <summary>按数据库键读取全部定义，目录外记录也保留并报告；重复键明确失败。</summary>
    private static async Task<Dictionary<string, ExistingPermission>> ReadExistingAsync(
        SqlConnection connection, SqlTransaction transaction)
    {
        var result = new Dictionary<string, ExistingPermission>(StringComparer.OrdinalIgnoreCase);
        await using var command = new SqlCommand("""
            SELECT Uid,PermissionKey,DisplayName,GroupKey,SortOrder
            FROM dbo.SysPermissionDefinitions WITH(UPDLOCK,HOLDLOCK);
            """, connection, transaction);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var item = new ExistingPermission(reader.GetGuid(0), reader.GetString(1),
                reader.GetString(2), reader.GetString(3), reader.GetInt32(4));
            if (!result.TryAdd(item.Key, item))
                throw new InvalidOperationException($"数据库权限定义存在重复键：{item.Key}。");
        }
        return result;
    }

    /// <summary>新键只建定义行，不创建角色关系或管理员账号。</summary>
    private static async Task InsertAsync(SqlConnection connection, SqlTransaction transaction,
        PlannedPermission item)
    {
        await using var command = new SqlCommand("""
            INSERT dbo.SysPermissionDefinitions
              (Uid,Version,IsDeleted,CreatedDate,CreatedBy,UpdatedDate,UpdatedBy,
               PermissionKey,DisplayName,GroupKey,SortOrder,IsActive)
            VALUES
              (@Uid,1,0,SYSUTCDATETIME(),N'DbSeed',SYSUTCDATETIME(),N'DbSeed',
               @PermissionKey,@DisplayName,@GroupKey,@SortOrder,1);
            """, connection, transaction);
        command.Parameters.AddWithValue("@Uid", Guid.NewGuid());
        BindDefinition(command, item);
        if (await command.ExecuteNonQueryAsync() != 1)
            throw new InvalidOperationException($"插入权限定义失败：{item.Definition.PermissionKey}。");
    }

    /// <summary>已有键仅改名称、分组和排序，不改 Uid、启停、删除标记及关系。</summary>
    private static async Task UpdateAsync(SqlConnection connection, SqlTransaction transaction,
        PlannedPermission item)
    {
        await using var command = new SqlCommand("""
            UPDATE dbo.SysPermissionDefinitions
            SET DisplayName=@DisplayName,GroupKey=@GroupKey,SortOrder=@SortOrder
            WHERE Uid=@Uid AND PermissionKey=@PermissionKey;
            """, connection, transaction);
        command.Parameters.AddWithValue("@Uid", item.Existing!.Uid);
        BindDefinition(command, item);
        if (await command.ExecuteNonQueryAsync() != 1)
            throw new InvalidOperationException($"更新权限定义失败：{item.Definition.PermissionKey}。");
    }

    private static void BindDefinition(SqlCommand command, PlannedPermission item)
    {
        command.Parameters.AddWithValue("@PermissionKey", item.Definition.PermissionKey);
        command.Parameters.AddWithValue("@DisplayName", item.Definition.DisplayName);
        command.Parameters.AddWithValue("@GroupKey", item.Definition.GroupKey);
        command.Parameters.AddWithValue("@SortOrder", item.SortOrder);
    }
}
