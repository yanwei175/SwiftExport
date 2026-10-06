using Dapper;
using SwiftExport.Contracts.DTOs.MasterData.ExecutionEligibility;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Core.Common;
using SwiftExport.Core.Interfaces.MasterData;
using SwiftExport.Infrastructure.Contexts;

namespace SwiftExport.Infrastructure.Repositories.MasterData;

/// <summary>使用既有主数据表和有效角色权限管理执行资格；表名只来自固定枚举白名单。</summary>
public sealed class ExecutionEligibilityRepository(RepositoryBaseContext context) : IExecutionEligibilityRepository
{
    /// <summary>源SQL Server继续使用原授权HOLDLOCK及目标UPDLOCK读取；准备阶段不增加SQL或改变源事务。</summary>
    public Task PrepareTargetBeforeSnapshotAsync(ExecutionEligibilityKind kind, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    /// <summary>在当前事务中锁定活动身份及角色权限，防止授权撤销与资格修改竞态。</summary>
    public async Task<bool> HasExecutionEligibilityPermissionAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT CAST(CASE WHEN EXISTS (
                SELECT 1 FROM dbo.SysUser u WITH(HOLDLOCK)
                INNER JOIN dbo.SysUser_Roles ur WITH(HOLDLOCK) ON ur.UserId=u.Uid
                INNER JOIN dbo.SysRoles r WITH(HOLDLOCK) ON r.Uid=ur.RoleId
                WHERE u.Uid=@UserId AND u.IsActive=1 AND ISNULL(u.IsDeleted,0)=0
                  AND ISNULL(ur.IsDeleted,0)=0 AND ISNULL(r.IsDeleted,0)=0
                  AND r.IsActive=1 AND (
                      r.RoleCode=N'SYSTEM_ADMIN' OR EXISTS (
                          SELECT 1 FROM dbo.SysRole_Permissions rp WITH(HOLDLOCK)
                          JOIN dbo.SysPermissionDefinitions p WITH(HOLDLOCK) ON p.Uid=rp.PermissionId
                          WHERE rp.RoleId=r.Uid AND ISNULL(rp.IsDeleted,0)=0
                            AND p.PermissionKey=N'MasterData.Eligibility.Manage'
                            AND p.IsActive=1 AND ISNULL(p.IsDeleted,0)=0
                      )
                  )
            ) THEN 1 ELSE 0 END AS bit);
            """;
        return await context.Session.Connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            sql, new { UserId = userId }, context.Session.Transaction, cancellationToken: cancellationToken));
    }

    /// <summary>锁定指定主数据的当前资格和版本，不将整个主数据暴露为命令上下文。</summary>
    public Task<ExecutionEligibilityStatusResult?> GetForUpdateAsync(
        ExecutionEligibilityKind kind, Guid uid, CancellationToken cancellationToken = default)
    {
        var table = GetTable(kind);
        return context.Session.Connection.QuerySingleOrDefaultAsync<ExecutionEligibilityStatusResult>(new CommandDefinition(
            $"SELECT @Kind AS Kind,Uid,Version,IsActive FROM dbo.[{table}] WITH(UPDLOCK,HOLDLOCK) WHERE Uid=@Uid AND ISNULL(IsDeleted,0)=0;",
            new { Kind = (int)kind, Uid = uid }, context.Session.Transaction, cancellationToken: cancellationToken));
    }

    /// <summary>以预期版本更新资格并由审计服务生成更新时间和操作人。</summary>
    public Task<int> SetStatusAsync(SetExecutionEligibilityStatusCommand command, CancellationToken cancellationToken = default)
    {
        var table = GetTable(command.Kind);
        // 审计载体只用于通用审计字段，不把客户端资料映射成可写的主数据实体。
        var stamp = new AuditFields();
        context.AuditStamp.StampForUpdate(stamp);
        return context.Session.Connection.ExecuteAsync(new CommandDefinition(
            $"""
            UPDATE dbo.[{table}] SET IsActive=@IsActive,Version=Version+1,UpdatedDate=@UpdatedDate,UpdatedBy=@UpdatedBy
            WHERE Uid=@Uid AND Version=@ExpectedVersion AND ISNULL(IsDeleted,0)=0;
            """, new { command.IsActive, command.Uid, command.ExpectedVersion, stamp.UpdatedDate, stamp.UpdatedBy },
            context.Session.Transaction, cancellationToken: cancellationToken));
    }

    /// <summary>枚举值仅允许三个明确对象，绝不拼接请求提供的表名。</summary>
    private static string GetTable(ExecutionEligibilityKind kind) => kind switch
    {
        ExecutionEligibilityKind.Product => "Product",
        ExecutionEligibilityKind.Customer => "Customer",
        ExecutionEligibilityKind.Supplier => "Supplier",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), "不支持的主数据资格对象。")
    };

    /// <summary>只承接审计服务生成的字段，不代表或持久化为额外业务实体。</summary>
    private sealed class AuditFields : BaseEntity;
}
