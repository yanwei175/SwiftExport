using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Npgsql;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Tools.DbSeed;

var connectionString = Environment.GetEnvironmentVariable("SWIFTEXPORT_DBSEED_CONNECTION");
var userName = Environment.GetEnvironmentVariable("SWIFTEXPORT_DBSEED_USERNAME")?.Trim();
var passwordHash = Environment.GetEnvironmentVariable("SWIFTEXPORT_DBSEED_PASSWORD_HASH");
var nickName = Environment.GetEnvironmentVariable("SWIFTEXPORT_DBSEED_NICKNAME")?.Trim();
if (string.IsNullOrWhiteSpace(connectionString))
    throw new InvalidOperationException("缺少 SWIFTEXPORT_DBSEED_CONNECTION；DbSeed 不使用内置连接信息。");
if (args.Length > 0)
{
    if (args[0] != "--sync-permissions" || args.Length > 2
        || (args.Length == 2 && args[1] != "--dry-run"))
        throw new ArgumentException("用法：DbSeed [--sync-permissions [--dry-run]]。");
    using var session = new DbSession(new PostgreSqlConnectionFactory(connectionString));
    await new PostgreSqlPermissionCatalogSync(session).RunAsync(args.Length == 2);
    return;
}
if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(passwordHash))
    throw new InvalidOperationException("必须同时设置 SWIFTEXPORT_DBSEED_USERNAME 和 SWIFTEXPORT_DBSEED_PASSWORD_HASH。");

var definitions = PermissionCatalog.All;
if (definitions.Select(item => item.PermissionKey).Distinct(StringComparer.Ordinal).Count() != definitions.Count)
    throw new InvalidOperationException("代码权限目录包含重复的权限键。");

await using var connection = new NpgsqlConnection(connectionString);
await connection.OpenAsync();
await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable);
try
{
    // 与账号、RBAC、恢复及同步工具一样，在首次快照前按角色→权限顺序排队；保护不存在的管理员/定义范围。
    // 仅缺表转为原结构诊断，不隐藏锁等待、取消或序列化错误，也不创建结构。
    try
    {
        await using var roles = Command(connection, transaction, "LOCK TABLE public.sys_role IN SHARE ROW EXCLUSIVE MODE;");
        await roles.ExecuteNonQueryAsync();
        await using var permissions = Command(connection, transaction, "LOCK TABLE public.sys_permission IN SHARE ROW EXCLUSIVE MODE;");
        await permissions.ExecuteNonQueryAsync();
    }
    catch (PostgresException error) when (error.SqlState == PostgresErrorCodes.UndefinedTable)
    {
        throw new InvalidOperationException("目标库不是当前五实体权限结构；DbSeed 不做结构迁移。", error);
    }

    // 引导只接受实体驱动的新结构；表或关键列缺失时直接失败，不执行隐式迁移。
    await using (var schema = Command(connection, transaction, """
        SELECT to_regclass('public.sys_user') IS NOT NULL
          AND to_regclass('public.sys_role') IS NOT NULL
          AND to_regclass('public.sys_user_role') IS NOT NULL
          AND to_regclass('public.sys_permission') IS NOT NULL
          AND to_regclass('public.sys_role_permission') IS NOT NULL
          AND EXISTS(SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='sys_role' AND column_name='role_code')
          AND EXISTS(SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='sys_permission' AND column_name='permission_key');
        """))
    {
        if (!(bool)(await schema.ExecuteScalarAsync() ?? false))
            throw new InvalidOperationException("目标库不是当前五实体权限结构；DbSeed 不做结构迁移。");
    }

    var knownKeys = new HashSet<string>(definitions.Select(item => item.PermissionKey), StringComparer.Ordinal);
    await using (var existing = Command(connection, transaction,
        "SELECT permission_key FROM public.sys_permission FOR UPDATE;"))
    await using (var reader = await existing.ExecuteReaderAsync())
    {
        while (await reader.ReadAsync())
        {
            var key = reader.GetString(0);
            if (!knownKeys.Contains(key))
                throw new InvalidOperationException($"数据库存在代码目录外的权限键：{key}。");
        }
    }

    // 目录是代码权威源；同键定义有差异时报告失败，避免静默覆盖已有授权含义。
    for (var index = 0; index < definitions.Count; index++)
    {
        var definition = definitions[index];
        var permissionId = Guid.Empty;
        await using (var select = Command(connection, transaction, """
            SELECT uid,display_name,group_key,sort_order,is_active,is_deleted
            FROM public.sys_permission
            WHERE rtrim(permission_key, ' ') COLLATE public.erp_dictionary_ci
                = rtrim(@PermissionKey, ' ') COLLATE public.erp_dictionary_ci FOR UPDATE;
            """))
        {
            select.Parameters.AddWithValue("@PermissionKey", definition.PermissionKey);
            await using var reader = await select.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                permissionId = reader.GetGuid(0);
                if (!string.Equals(reader.GetString(1), definition.DisplayName, StringComparison.Ordinal)
                    || !string.Equals(reader.GetString(2), definition.GroupKey, StringComparison.Ordinal)
                    || reader.GetInt32(3) != index + 1 || !reader.GetBoolean(4) || reader.GetBoolean(5))
                    throw new InvalidOperationException($"数据库权限定义与代码目录不一致：{definition.PermissionKey}。");
            }
        }

        if (permissionId != Guid.Empty)
            continue;
        await using var insert = Command(connection, transaction, """
            INSERT INTO public.sys_permission
              (uid,version,is_deleted,created_date,created_by,updated_date,updated_by,
               permission_key,display_name,group_key,sort_order,is_active)
            VALUES
              (@Uid,1,FALSE,clock_timestamp() AT TIME ZONE 'UTC','DbSeed',clock_timestamp() AT TIME ZONE 'UTC','DbSeed',
               @PermissionKey,@DisplayName,@GroupKey,@SortOrder,TRUE);
            """);
        insert.Parameters.AddWithValue("@Uid", Guid.NewGuid());
        insert.Parameters.AddWithValue("@PermissionKey", definition.PermissionKey);
        insert.Parameters.AddWithValue("@DisplayName", definition.DisplayName);
        insert.Parameters.AddWithValue("@GroupKey", definition.GroupKey);
        insert.Parameters.AddWithValue("@SortOrder", index + 1);
        await insert.ExecuteNonQueryAsync();
    }

    var adminRoleId = Guid.Empty;
    await using (var select = Command(connection, transaction, """
        SELECT uid,is_active,is_system,is_deleted FROM public.sys_role
        WHERE rtrim(role_code, ' ') COLLATE public.erp_dictionary_ci
            = rtrim(@RoleCode, ' ') COLLATE public.erp_dictionary_ci FOR UPDATE;
        """))
    {
        select.Parameters.AddWithValue("@RoleCode", SystemRoleCodes.SystemAdmin);
        await using var reader = await select.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            adminRoleId = reader.GetGuid(0);
            if (!reader.GetBoolean(1) || !reader.GetBoolean(2) || reader.GetBoolean(3))
                throw new InvalidOperationException("既有 SYSTEM_ADMIN 角色状态异常，拒绝自动修复。");
        }
    }
    if (adminRoleId == Guid.Empty)
    {
        adminRoleId = Guid.NewGuid();
        await using var insert = Command(connection, transaction, """
            INSERT INTO public.sys_role
              (uid,version,is_deleted,created_date,created_by,updated_date,updated_by,
               role_code,role_name,description,is_active,is_system)
            VALUES
              (@Uid,1,FALSE,clock_timestamp() AT TIME ZONE 'UTC','DbSeed',clock_timestamp() AT TIME ZONE 'UTC','DbSeed',
               @RoleCode,'系统管理员','内置系统管理员',TRUE,TRUE);
            """);
        insert.Parameters.AddWithValue("@Uid", adminRoleId);
        insert.Parameters.AddWithValue("@RoleCode", SystemRoleCodes.SystemAdmin);
        await insert.ExecuteNonQueryAsync();
    }

    var adminUserId = Guid.Empty;
    var existingUser = false;
    await using (var select = Command(connection, transaction, """
        SELECT uid,is_active,is_deleted FROM public.sys_user
        WHERE rtrim(user_name, ' ') COLLATE public.erp_dictionary_ci
            = rtrim(@UserName, ' ') COLLATE public.erp_dictionary_ci FOR UPDATE;
        """))
    {
        select.Parameters.AddWithValue("@UserName", userName);
        await using var reader = await select.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            existingUser = true;
            adminUserId = reader.GetGuid(0);
            if (!reader.GetBoolean(1) || reader.GetBoolean(2))
                throw new InvalidOperationException("引导用户名已被停用或删除账号占用，拒绝自动提升。");
        }
    }
    if (existingUser)
    {
        await using var check = Command(connection, transaction, """
            SELECT EXISTS(SELECT 1 FROM public.sys_user_role
              WHERE user_id=@UserId AND role_id=@RoleId AND is_deleted=FALSE FOR UPDATE);
            """);
        check.Parameters.AddWithValue("@UserId", adminUserId);
        check.Parameters.AddWithValue("@RoleId", adminRoleId);
        if (!(bool)(await check.ExecuteScalarAsync() ?? false))
            throw new InvalidOperationException("引导用户名已属于普通账号，拒绝静默提升为管理员。");
    }
    else
    {
        adminUserId = Guid.NewGuid();
        await using var insertUser = Command(connection, transaction, """
            INSERT INTO public.sys_user
              (uid,version,is_deleted,created_date,created_by,updated_date,updated_by,
               user_name,nick_name,password_hash,credential_version,is_active)
            VALUES
              (@Uid,1,FALSE,clock_timestamp() AT TIME ZONE 'UTC','DbSeed',clock_timestamp() AT TIME ZONE 'UTC','DbSeed',
               @UserName,@NickName,@PasswordHash,1,TRUE);
            """);
        insertUser.Parameters.AddWithValue("@Uid", adminUserId);
        insertUser.Parameters.AddWithValue("@UserName", userName);
        insertUser.Parameters.AddWithValue("@NickName", (object?)nickName ?? userName);
        insertUser.Parameters.AddWithValue("@PasswordHash", passwordHash);
        await insertUser.ExecuteNonQueryAsync();

        await using var insertRole = Command(connection, transaction, """
            INSERT INTO public.sys_user_role
              (uid,version,is_deleted,created_date,created_by,updated_date,updated_by,user_id,role_id)
            VALUES
              (@Uid,1,FALSE,clock_timestamp() AT TIME ZONE 'UTC','DbSeed',clock_timestamp() AT TIME ZONE 'UTC','DbSeed',@UserId,@RoleId);
            """);
        insertRole.Parameters.AddWithValue("@Uid", Guid.NewGuid());
        insertRole.Parameters.AddWithValue("@UserId", adminUserId);
        insertRole.Parameters.AddWithValue("@RoleId", adminRoleId);
        await insertRole.ExecuteNonQueryAsync();
    }

    await transaction.CommitAsync();
    Console.WriteLine(existingUser
        ? $"权限目录已核对（{definitions.Count} 项）；既有管理员 {userName} 未修改密码。"
        : $"权限目录已初始化（{definitions.Count} 项）；管理员 {userName} 已建立并关联内置角色。");
}
catch
{
    try { await transaction.RollbackAsync(); }
    catch { /* 原始失败优先；连接释放会清理事务。 */ }
    throw;
}

/// <summary>将所有引导 SQL 绑定到同一连接与串行化事务。</summary>
static NpgsqlCommand Command(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql) =>
    new NpgsqlCommand(sql, connection, transaction);
