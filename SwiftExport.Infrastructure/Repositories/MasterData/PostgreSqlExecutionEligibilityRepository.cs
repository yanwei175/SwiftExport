using Dapper;
using SwiftExport.Contracts.DTOs.MasterData.ExecutionEligibility;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Core.Common;
using SwiftExport.Core.Interfaces.MasterData;
using SwiftExport.Infrastructure.Contexts;

namespace SwiftExport.Infrastructure.Repositories.MasterData;

/// <summary>在调用方事务中翻译执行资格的授权锁、三类主数据读取和预期版本更新；不拥有业务事务。</summary>
public sealed class PostgreSqlExecutionEligibilityRepository(RepositoryBaseContext context) : IExecutionEligibilityRepository
{
    /// <summary>首次SELECT/DML前仅锁当前Kind主表，排队写入及显式行锁读取后再建立快照；普通SELECT仍可并行。</summary>
    public async Task PrepareTargetBeforeSnapshotAsync(ExecutionEligibilityKind kind, CancellationToken cancellationToken = default)
    {
        var table = GetTable(kind);
        RequireTransaction();
        // 必须先目标再授权目录：相反顺序会与产品→供应商等已有持锁事务形成跨表反向等待。
        // EXCLUSIVE冲突ROW SHARE/ROW EXCLUSIVE，避免较弱表锁与已有行锁持有者互相等待升级；只锁当前三值白名单中的一表。
        await context.Session.Connection.ExecuteAsync(Command($"LOCK TABLE public.{table} IN EXCLUSIVE MODE;", null, cancellationToken));
    }

    /// <summary>在首次查询前排队角色目录及权限定义，再共享锁定原活动授权；管理员编码分支不要求内置标记。</summary>
    public async Task<bool> HasExecutionEligibilityPermissionAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        RequireTransaction();
        // 复用RBAC角色目录锁顺序，在首个快照前等待；权限定义的SHARE锁也保护未找到权限时的空范围。
        // 不刷新已有快照、不重试40001；普通主数据CRUD竞争仍须完整服务实库验收。
        await context.Session.Connection.ExecuteAsync(Command("""
            LOCK TABLE public.sys_role IN SHARE ROW EXCLUSIVE MODE;
            LOCK TABLE public.sys_permission IN SHARE MODE;
            """, null, cancellationToken));
        var actor = await context.Session.Connection.QuerySingleOrDefaultAsync<Guid?>(Command("""
            SELECT uid FROM public.sys_user
            WHERE uid=@UserId AND is_active=TRUE AND COALESCE(is_deleted,FALSE)=FALSE FOR SHARE;
            """, new { UserId = userId }, cancellationToken));
        if (!actor.HasValue) return false;
        var roles = (await context.Session.Connection.QueryAsync<AuthorizationRole>(Command("""
            SELECT r.uid AS "Uid",(rtrim(r.role_code,' ') COLLATE public.erp_dictionary_ci='SYSTEM_ADMIN') AS "IsAdmin"
            FROM public.sys_user_role ur JOIN public.sys_role r ON r.uid=ur.role_id
            WHERE ur.user_id=@UserId AND COALESCE(ur.is_deleted,FALSE)=FALSE
              AND r.is_active=TRUE AND COALESCE(r.is_deleted,FALSE)=FALSE
            ORDER BY r.uid,ur.uid FOR SHARE OF ur,r;
            """, new { UserId = userId }, cancellationToken))).AsList();
        if (roles.Any(role => role.IsAdmin)) return true;
        if (roles.Count == 0) return false;
        var matches = await context.Session.Connection.QueryAsync<Guid>(Command("""
            SELECT p.uid FROM public.sys_role_permission rp JOIN public.sys_permission p ON p.uid=rp.permission_id
            WHERE rp.role_id=ANY(@RoleIds) AND COALESCE(rp.is_deleted,FALSE)=FALSE
              AND rtrim(p.permission_key,' ') COLLATE public.erp_dictionary_ci='MasterData.Eligibility.Manage'
              AND p.is_active=TRUE AND COALESCE(p.is_deleted,FALSE)=FALSE
            ORDER BY p.uid,rp.uid FOR SHARE OF rp,p;
            """, new { RoleIds = roles.Select(role => role.Uid).Distinct().ToArray() }, cancellationToken));
        return matches.Any();
    }

    /// <summary>通过固定枚举白名单锁定未软删除目标的当前状态和版本；未知枚举保留源实现异常。</summary>
    public Task<ExecutionEligibilityStatusResult?> GetForUpdateAsync(
        ExecutionEligibilityKind kind, Guid uid, CancellationToken cancellationToken = default)
    {
        var table = GetTable(kind);
        RequireTransaction();
        return context.Session.Connection.QuerySingleOrDefaultAsync<ExecutionEligibilityStatusResult>(Command(
            $"SELECT @Kind AS \"Kind\",uid AS \"Uid\",version AS \"Version\",is_active AS \"IsActive\" FROM public.{table} WHERE uid=@Uid AND COALESCE(is_deleted,FALSE)=FALSE FOR UPDATE;",
            new { Kind = (int)kind, Uid = uid }, cancellationToken));
    }

    /// <summary>仅按预期版本更新目标资格、版本及既有审计字段；零行交由原服务判定并发冲突。</summary>
    public Task<int> SetStatusAsync(SetExecutionEligibilityStatusCommand command, CancellationToken cancellationToken = default)
    {
        var table = GetTable(command.Kind);
        RequireTransaction();
        var stamp = new AuditFields();
        context.AuditStamp.StampForUpdate(stamp);
        return context.Session.Connection.ExecuteAsync(Command($"""
            UPDATE public.{table} SET is_active=@IsActive,version=version+1,updated_date=@UpdatedDate,updated_by=@UpdatedBy
            WHERE uid=@Uid AND version=@ExpectedVersion AND COALESCE(is_deleted,FALSE)=FALSE;
            """, new { command.IsActive, command.Uid, command.ExpectedVersion, stamp.UpdatedDate, stamp.UpdatedBy }, cancellationToken));
    }

    /// <summary>每条命令借用同一请求连接和事务，所有等待保留调用方取消令牌。</summary>
    private CommandDefinition Command(string sql, object? parameters, CancellationToken token)
        => new(sql, parameters, context.Session.Transaction, cancellationToken: token);

    /// <summary>PG持有锁必须有外层事务；实际执行资格服务始终拥有该事务，仓储不得自行补事务。</summary>
    private void RequireTransaction()
    {
        if (context.Session.Transaction is null)
            throw new InvalidOperationException("执行资格仓储锁定和写入需要调用方事务。");
    }

    /// <summary>只允许源实现已有的三个对象，不接受客户端表名或创建额外实体。</summary>
    private static string GetTable(ExecutionEligibilityKind kind) => kind switch
    {
        ExecutionEligibilityKind.Product => "product",
        ExecutionEligibilityKind.Customer => "customer",
        ExecutionEligibilityKind.Supplier => "supplier",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), "不支持的主数据资格对象。")
    };

    /// <summary>局部授权行投影，仅承接数据库判定的管理员编码和角色身份。</summary>
    private sealed class AuthorizationRole
    {
        public Guid Uid { get; set; }
        public bool IsAdmin { get; set; }
    }

    /// <summary>只承接既有审计打戳服务，不映射或持久化为新的业务表。</summary>
    private sealed class AuditFields : BaseEntity;
}
