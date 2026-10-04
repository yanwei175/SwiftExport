using Dapper;
using SwiftExport.Core.Entities.Commands;
using SwiftExport.Core.Interfaces.Commands;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.Commands;

/// <summary>在共享 PostgreSQL 事务内锁定命令身份并追加不可变回执；不接管调用方事务。</summary>
public sealed class PostgreSqlCommandReceiptRepository(RepositoryBaseContext context) : ICommandReceiptRepository
{
    private static readonly EntityMetadata Metadata = PostgreSqlEntityMetadata.GetMetadata(typeof(CommandReceipt));
    private static readonly string Table = $"{PostgreSqlNames.Quote(Metadata.SchemaName)}.{PostgreSqlNames.Quote(Metadata.TableName)}";
    private static readonly string Projection = string.Join(", ", Metadata.Columns.Select(column =>
        $"{PostgreSqlNames.Quote(column.ColumnName)} AS {PostgreSqlNames.Quote(column.PropertyName)}"));

    /// <summary>委托共享事务命令锁；仅支持 ReadCommitted 及等效 ReadUncommitted，更高隔离级别因旧快照风险明确拒绝。</summary>
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
        var columns = string.Join(", ", Metadata.Columns.Select(column => PostgreSqlNames.Quote(column.ColumnName)));
        var values = string.Join(", ", Metadata.Columns.Select(column => "@" + column.ParameterName));
        // 禁止 UPSERT 或覆盖旧回执；唯一约束、只追加触发器及数据库异常均保留给外层处理。
        var rows = await context.Session.Connection.ExecuteAsync(new CommandDefinition(
            $"INSERT INTO {Table} ({columns}) VALUES ({values});", PostgreSqlParameters.Entity(receipt, Metadata),
            transaction, cancellationToken: token));
        if (rows != 1)
            throw new InvalidOperationException("命令回执追加未写入一行，业务事务不能提交。");
    }
}
