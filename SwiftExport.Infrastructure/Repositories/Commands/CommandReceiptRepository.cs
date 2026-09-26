using Dapper;
using SwiftExport.Core.Entities.Commands;
using SwiftExport.Core.Interfaces.Commands;
using SwiftExport.Infrastructure.Contexts;

namespace SwiftExport.Infrastructure.Repositories.Commands;

/// <summary>使用共享 DbSession 在同一 SQL Server 事务内锁定并追加命令回执。</summary>
public sealed class CommandReceiptRepository(RepositoryBaseContext context) : ICommandReceiptRepository
{
    /// <inheritdoc/>
    public Task<int> LockAsync(Guid commandId, CancellationToken token)
    {
        if (context.Session.Transaction is null) throw new InvalidOperationException("命令锁必须在业务事务内获取。");
        const string sql = """
            DECLARE @Result int;
            EXEC @Result=sys.sp_getapplock @Resource=@Resource,@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=10000;
            SELECT @Result;
            """;
        return context.Session.Connection.ExecuteScalarAsync<int>(new CommandDefinition(sql,
            new { Resource = $"SwiftExport:Command:{commandId:N}" }, context.Session.Transaction, cancellationToken: token));
    }

    /// <inheritdoc/>
    public Task<CommandReceipt?> FindAsync(Guid commandId, CancellationToken token)
    {
        const string sql = """
            SELECT Uid,Version,IsDeleted,CreatedDate,CreatedBy,UpdatedDate,UpdatedBy,
                   CommandId,ActorUid,CommandType,RequestTargetUid,FingerprintVersion,
                   RequestFingerprint,ResultVersion,ResultJson,RecordedAtUtc
            FROM dbo.CommandReceipt WITH(UPDLOCK,HOLDLOCK) WHERE CommandId=@CommandId;
            """;
        return context.Session.Connection.QuerySingleOrDefaultAsync<CommandReceipt>(new CommandDefinition(sql,
            new { CommandId = commandId }, context.Session.Transaction, cancellationToken: token));
    }

    /// <inheritdoc/>
    public async Task InsertAsync(CommandReceipt receipt, CancellationToken token)
    {
        if (context.Session.Transaction is null) throw new InvalidOperationException("回执必须与业务事实一起提交。");
        context.AuditStamp.StampForInsert(receipt);
        const string sql = """
            INSERT dbo.CommandReceipt(Uid,Version,IsDeleted,CreatedDate,CreatedBy,UpdatedDate,UpdatedBy,
                CommandId,ActorUid,CommandType,RequestTargetUid,FingerprintVersion,RequestFingerprint,
                ResultVersion,ResultJson,RecordedAtUtc)
            VALUES(@Uid,@Version,@IsDeleted,@CreatedDate,@CreatedBy,@UpdatedDate,@UpdatedBy,
                @CommandId,@ActorUid,@CommandType,@RequestTargetUid,@FingerprintVersion,@RequestFingerprint,
                @ResultVersion,@ResultJson,@RecordedAtUtc);
            """;
        await context.Session.Connection.ExecuteAsync(new CommandDefinition(sql, receipt,
            context.Session.Transaction, cancellationToken: token));
    }
}
