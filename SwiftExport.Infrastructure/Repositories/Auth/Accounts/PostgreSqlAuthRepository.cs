using Dapper;
using SwiftExport.Core.Entities.Auth;
using SwiftExport.Core.Interfaces.Auth.Accounts;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.Auth.Accounts;

/// <summary>在当前 PostgreSQL 会话中维护认证账号；保留逐语句预判、最终授权和调用方事务边界。</summary>
public sealed class PostgreSqlAuthRepository(RepositoryBaseContext context) : IAuthRepository
{
    private static readonly EntityMetadata Metadata = PostgreSqlEntityMetadata.GetMetadata(typeof(SysUser));
    private static readonly string Users = Table(typeof(SysUser));
    private static readonly string UserRoles = Table(typeof(SysUserRole));
    private static readonly string Roles = Table(typeof(SysRole));
    private static readonly string Columns = string.Join(", ", Metadata.Columns.Select(column =>
        $"{PostgreSqlNames.Quote(column.ColumnName)} AS {PostgreSqlNames.Quote(column.PropertyName)}"));

    /// <summary>按已确认的尾空格及 ICU 比较规则读取活动账号，保留完整凭据字段。</summary>
    public Task<SysUser?> GetActiveUserByNameAsync(string userName, CancellationToken cancellationToken = default) =>
        context.Session.Connection.QuerySingleOrDefaultAsync<SysUser>(Command($"""
            SELECT {Columns} FROM {Users}
            WHERE {NameMatch} AND is_active = TRUE AND COALESCE(is_deleted, FALSE) = FALSE;
            """, new { UserName = userName }, cancellationToken));

    /// <summary>按标识读取活动且未删除的账号和凭据。</summary>
    public Task<SysUser?> GetActiveUserByIdAsync(Guid userId, CancellationToken cancellationToken = default) =>
        context.Session.Connection.QuerySingleOrDefaultAsync<SysUser>(Command($"""
            SELECT {Columns} FROM {Users}
            WHERE uid = @UserId AND is_active = TRUE AND COALESCE(is_deleted, FALSE) = FALSE;
            """, new { UserId = userId }, cancellationToken));

    /// <summary>判断用户、角色及关联均有效的 SYSTEM_ADMIN 授权。</summary>
    public Task<bool> IsAdminAsync(Guid userId, CancellationToken cancellationToken = default) =>
        context.Session.Connection.ExecuteScalarAsync<bool>(Command(
            $"SELECT EXISTS({AdminQuery("@UserId")});", new { UserId = userId }, cancellationToken));

    /// <summary>判断未删除用户名是否占用；停用账号仍保留用户名。</summary>
    public Task<bool> IsUserNameInUseAsync(string userName, CancellationToken cancellationToken = default) =>
        context.Session.Connection.ExecuteScalarAsync<bool>(Command($"""
            SELECT EXISTS(SELECT 1 FROM {Users}
                WHERE {NameMatch} AND COALESCE(is_deleted, FALSE) = FALSE);
            """, new { UserName = userName }, cancellationToken));

    /// <summary>先判重名，再在 INSERT 中重验管理员；定向用户名冲突不破坏外层事务，其他唯一失败向上抛出。</summary>
    public async Task<AuthUserCreateStatus> CreateUserByAdminAsync(
        Guid actorUserId, SysUser user, CancellationToken cancellationToken = default)
    {
        context.AuditStamp.StampForInsert(user);
        if (await IsUserNameInUseAsync(user.UserName, cancellationToken))
            return AuthUserCreateStatus.UserNameInUse;

        var parameters = PostgreSqlParameters.Entity(user, Metadata);
        parameters.Add("ActorUserId", actorUserId);
        var inserted = await context.Session.Connection.QuerySingleOrDefaultAsync<Guid?>(Command($"""
            INSERT INTO {Users}
                (uid, version, is_deleted, created_date, created_by, updated_date, updated_by,
                 user_name, nick_name, password_hash, credential_version, email, phone_number, is_active)
            SELECT @Uid, @Version, FALSE, @CreatedDate, @CreatedBy, @UpdatedDate, @UpdatedBy,
                   @UserName, @NickName, @PasswordHash, @CredentialVersion, @Email, @PhoneNumber, TRUE
            WHERE EXISTS({AdminQuery("@ActorUserId")})
            ON CONFLICT ((rtrim(user_name, ' ')) COLLATE public.erp_dictionary_ci)
                WHERE is_deleted = FALSE DO NOTHING
            RETURNING uid;
            """, parameters, cancellationToken));
        if (inserted.HasValue) return AuthUserCreateStatus.Created;

        // 零行可能来自晚到的用户名竞争或授权失效；保留重名优先的返回结果。
        return await IsUserNameInUseAsync(user.UserName, cancellationToken)
            ? AuthUserCreateStatus.UserNameInUse
            : AuthUserCreateStatus.ActorNotAdmin;
    }

    /// <summary>按双版本原子修改本人密码和审计字段；成功时数据库版本各递增一次，不回填输入实体版本。</summary>
    public async Task<bool> UpdateOwnPasswordAsync(SysUser user, CancellationToken cancellationToken = default)
    {
        context.AuditStamp.StampForUpdate(user);
        var rows = await context.Session.Connection.ExecuteAsync(Command($"""
            UPDATE {Users}
            SET password_hash = @PasswordHash, credential_version = credential_version + 1,
                version = version + 1, updated_date = @UpdatedDate, updated_by = @UpdatedBy
            WHERE uid = @Uid AND version = @Version AND credential_version = @CredentialVersion
              AND is_active = TRUE AND COALESCE(is_deleted, FALSE) = FALSE;
            """, PostgreSqlParameters.Entity(user, Metadata), cancellationToken));
        return rows == 1;
    }

    /// <summary>按原顺序检查操作者、目标及管理员身份，最终 UPDATE 重验授权和双版本，不接管事务。</summary>
    public async Task<AdminPasswordResetStatus> ResetNonAdminPasswordAsync(
        Guid actorUserId, SysUser targetUser, CancellationToken cancellationToken = default)
    {
        context.AuditStamp.StampForUpdate(targetUser);
        if (!await IsAdminAsync(actorUserId, cancellationToken))
            return AdminPasswordResetStatus.ActorNotAdmin;
        if (await GetActiveUserByIdAsync(targetUser.Uid, cancellationToken) is null)
            return AdminPasswordResetStatus.TargetNotFound;
        if (await IsAdminAsync(targetUser.Uid, cancellationToken))
            return AdminPasswordResetStatus.TargetIsAdmin;

        var parameters = PostgreSqlParameters.Entity(targetUser, Metadata);
        parameters.Add("ActorUserId", actorUserId);
        var rows = await context.Session.Connection.ExecuteAsync(Command($"""
            UPDATE {Users} AS target
            SET password_hash = @PasswordHash, credential_version = target.credential_version + 1,
                version = target.version + 1, updated_date = @UpdatedDate, updated_by = @UpdatedBy
            WHERE target.uid = @Uid AND target.version = @Version
              AND target.credential_version = @CredentialVersion
              AND target.is_active = TRUE AND COALESCE(target.is_deleted, FALSE) = FALSE
              AND EXISTS({AdminQuery("@ActorUserId")})
              AND NOT EXISTS({AdminQuery("target.uid")});
            """, parameters, cancellationToken));
        return rows == 1 ? AdminPasswordResetStatus.Updated : AdminPasswordResetStatus.ConcurrencyConflict;
    }

    /// <summary>核对活动账号当前凭据版本，供 JWT 校验拒绝停用账号和旧密码令牌。</summary>
    public Task<bool> IsCredentialVersionValidAsync(
        Guid userId, int credentialVersion, CancellationToken cancellationToken = default) =>
        context.Session.Connection.ExecuteScalarAsync<bool>(Command($"""
            SELECT EXISTS(SELECT 1 FROM {Users}
                WHERE uid = @UserId AND credential_version = @CredentialVersion
                  AND is_active = TRUE AND COALESCE(is_deleted, FALSE) = FALSE);
            """, new { UserId = userId, CredentialVersion = credentialVersion }, cancellationToken));

    // 查询与唯一索引使用相同比较表达式；只去掉 ASCII 尾空格，不改写存储文本。
    private const string NameMatch = "rtrim(user_name, ' ') COLLATE public.erp_dictionary_ci = rtrim(@UserName, ' ') COLLATE public.erp_dictionary_ci";

    /// <summary>生成仅接受内部固定标识表达式的管理员查询，用户输入始终通过参数传递。</summary>
    private static string AdminQuery(string userIdExpression) => $"""
        SELECT 1 FROM {Users} AS actor
        JOIN {UserRoles} AS ur ON ur.user_id = actor.uid AND COALESCE(ur.is_deleted, FALSE) = FALSE
        JOIN {Roles} AS r ON r.uid = ur.role_id AND COALESCE(r.is_deleted, FALSE) = FALSE AND r.is_active = TRUE
        WHERE actor.uid = {userIdExpression} AND actor.is_active = TRUE
          AND COALESCE(actor.is_deleted, FALSE) = FALSE AND r.role_code = 'SYSTEM_ADMIN'
        """;

    /// <summary>由公共元数据取得独立 PostgreSQL 表名，不修改 SQL Server 映射。</summary>
    private static string Table(Type type)
    {
        var metadata = PostgreSqlEntityMetadata.GetMetadata(type);
        return $"{PostgreSqlNames.Quote(metadata.SchemaName)}.{PostgreSqlNames.Quote(metadata.TableName)}";
    }

    /// <summary>每条命令借用当前事务并传递取消令牌，允许无外层事务执行原分阶段语义。</summary>
    private CommandDefinition Command(string sql, object? parameters, CancellationToken cancellationToken) =>
        new(sql, parameters, context.Session.Transaction, cancellationToken: cancellationToken);
}
