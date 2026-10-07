using Dapper;
using Microsoft.Extensions.Logging;
using SwiftExport.Core.Entities.Commands;
using SwiftExport.Core.Interfaces.Commands;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.Commands;

/// <summary>在共享 PostgreSQL 事务内锁定命令身份并追加不可变回执；不接管调用方事务。</summary>
public sealed class PostgreSqlCommandReceiptRepository(RepositoryBaseContext context) : ICommandReceiptRepository, ICommandTransactionPreparation
{
    /// <summary>本仓储固定持久化列及参数，保留原写入范围。</summary>
    private const string InsertReceiptSql = """
        INSERT INTO "public"."command_receipt" ("uid",
        "version",
        "is_deleted",
        "created_date",
        "created_by",
        "updated_date",
        "updated_by",
        "command_id",
        "actor_uid",
        "command_type",
        "request_target_uid",
        "fingerprint_version",
        "request_fingerprint",
        "result_version",
        "result_json",
        "recorded_at_utc") VALUES (@Uid,
        @Version,
        @IsDeleted,
        @CreatedDate,
        @CreatedBy,
        @UpdatedDate,
        @UpdatedBy,
        @CommandId,
        @ActorUid,
        @CommandType,
        @RequestTargetUid,
        @FingerprintVersion,
        @RequestFingerprint,
        @ResultVersion,
        @ResultJson,
        @RecordedAtUtc);
    """;

    private static readonly EntityMetadata Metadata = EntityMetadataCache.GetMetadata(typeof(CommandReceipt));
    private static readonly string Table = $"{PostgreSqlNames.Quote(Metadata.SchemaName)}.{PostgreSqlNames.Quote(Metadata.TableName)}";
    private const string Projection = """
        "uid" AS "Uid",
        "version" AS "Version",
        "is_deleted" AS "IsDeleted",
        "created_date" AS "CreatedDate",
        "created_by" AS "CreatedBy",
        "updated_date" AS "UpdatedDate",
        "updated_by" AS "UpdatedBy",
        "command_id" AS "CommandId",
        "actor_uid" AS "ActorUid",
        "command_type" AS "CommandType",
        "request_target_uid" AS "RequestTargetUid",
        "fingerprint_version" AS "FingerprintVersion",
        "request_fingerprint" AS "RequestFingerprint",
        "result_version" AS "ResultVersion",
        "result_json" AS "ResultJson",
        "recorded_at_utc" AS "RecordedAtUtc"
    """;

    /// <summary>事务开始前获取同连接命令租约，竞争耗尽返回空；调用方必须在事务释放后释放租约。</summary>
    public Task<IAsyncDisposable?> AcquireBeforeTransactionAsync(Guid commandId, CancellationToken token) =>
        PostgreSqlCommandLock.AcquireBeforeTransactionAsync(context.Session, commandId,
            context.LoggerFactory.CreateLogger<PostgreSqlCommandReceiptRepository>(), token);

    /// <summary>验证同身份事务前租约；没有租约时仅支持 ReadCommitted 及等效 ReadUncommitted。</summary>
    public Task<int> LockAsync(Guid commandId, CancellationToken token) =>
        PostgreSqlCommandLock.AcquireAsync(context.Session, commandId, token);

    /// <summary>按完整 CommandId 读取不可变原始回执，显式映射全部字段并传递当前事务和取消令牌。</summary>
    public Task<CommandReceipt?> FindAsync(Guid commandId, CancellationToken token)
    {
        var parameters = new PostgreSqlParameters();
        parameters.Add("CommandId", commandId);
        // 已存在的回执由只追加触发器保护；缺失回执的竞争由命令锁协议及唯一索引共同保护。
        return context.Session.Connection.QuerySingleOrDefaultAsync<CommandReceipt>(new CommandDefinition(
            $"SELECT {Projection} FROM {Table} WHERE command_id = @CommandId;", parameters,
            context.Session.Transaction, cancellationToken: token));
    }

    /// <summary>同业务事实一起追加成功回执；保留原身份和版本，按实体时间类型绑定审计时间及 UTC 时间。</summary>
    public async Task InsertAsync(CommandReceipt receipt, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var transaction = context.Session.Transaction
            ?? throw new InvalidOperationException("回执必须与业务事实一起提交。");
        context.AuditStamp.StampForInsert(receipt);
        // 禁止 UPSERT 或覆盖旧回执；唯一约束、只追加触发器及数据库异常均保留给外层处理。
        var rows = await context.Session.Connection.ExecuteAsync(new CommandDefinition(
            InsertReceiptSql, PostgreSqlParameters.Entity(receipt, Metadata),
            transaction, cancellationToken: token));
        if (rows != 1)
            throw new InvalidOperationException("命令回执追加未写入一行，业务事务不能提交。");
    }
}
