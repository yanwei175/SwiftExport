using Dapper;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrders;
using SwiftExport.Core.Entities.Auditing;
using SwiftExport.Core.Interfaces.Auditing;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.Auditing;

/// <summary>使用共享 PG 会话追加审计并兼容旧日志回执；命令查询和插入要求外层 RC 事务，暂不接入运行注册。</summary>
public sealed class PostgreSqlBusinessOperationLogRepository(RepositoryBaseContext context) : IBusinessOperationLogRepository
{
    private DbSession Session => context.Session;
    private static readonly EntityMetadata Metadata = PostgreSqlEntityMetadata.GetMetadata(typeof(BusinessOperationLog));
    private static readonly string Columns = string.Join(", ", Metadata.Columns.Select(c => $"{PostgreSqlNames.Quote(c.ColumnName)} AS {PostgreSqlNames.Quote(c.PropertyName)}"));
    private static readonly string InsertColumns = string.Join(", ", Metadata.Columns.Select(c => PostgreSqlNames.Quote(c.ColumnName)));
    private static readonly string InsertParameters = string.Join(", ", Metadata.Columns.Select(c => $"@{c.ParameterName}"));

    /// <summary>先锁全库命令身份以保护缺失键，再读取模块内最早日志，保留跨操作旧回执冲突判断。</summary>
    public async Task<BusinessOperationLog?> GetByModuleCommandAsync(string module, Guid commandId)
    {
        await LockCommandAsync(commandId);
        return await Session.Connection.QueryFirstOrDefaultAsync<BusinessOperationLog>(
            $"SELECT {Columns} FROM public.business_operation_log WHERE module=@Module AND command_id=@CommandId AND is_deleted=FALSE ORDER BY occurred_date, uid LIMIT 1 FOR UPDATE",
            new { Module = module, CommandId = commandId }, Session.Transaction);
    }

    /// <summary>取得同一命令锁后读取模块、操作和命令三元键，保留单行契约并锁定现存日志。</summary>
    public async Task<BusinessOperationLog?> GetByCommandAsync(string module, string operationType, Guid commandId)
    {
        await LockCommandAsync(commandId);
        return await Session.Connection.QuerySingleOrDefaultAsync<BusinessOperationLog>(
            $"SELECT {Columns} FROM public.business_operation_log WHERE module=@Module AND operation_type=@OperationType AND command_id=@CommandId AND is_deleted=FALSE FOR UPDATE",
            new { Module = module, OperationType = operationType, CommandId = commandId }, Session.Transaction);
    }

    /// <summary>在调用方 RC 事务及命令锁内打审计戳并追加日志；保留 Uid、版本、发生时间和原始 JSON 文本。</summary>
    public async Task<int> InsertAsync(BusinessOperationLog log)
    {
        await LockCommandAsync(log.CommandId);
        context.AuditStamp.StampForInsert(log);
        return await Session.Connection.ExecuteAsync(
            $"INSERT INTO public.business_operation_log ({InsertColumns}) VALUES ({InsertParameters})",
            PostgreSqlParameters.Entity(log, Metadata), Session.Transaction);
    }

    /// <summary>普通追溯不取命令锁；按模块及聚合过滤，显式投影用户名称并传递现有事务和取消令牌。</summary>
    public async Task<IReadOnlyList<BusinessOperationTraceItemDto>> GetByAggregateAsync(string module, Guid aggregateUid, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT uid AS "Uid", operation_type AS "OperationType", operation_name AS "OperationName",
                   reason AS "Reason", summary AS "Summary", changed_fields_json AS "ChangedFieldsJson",
                   related_objects_json AS "RelatedObjectsJson", user_name_snapshot AS "UserName",
                   occurred_date AS "OccurredDate", correlation_id AS "CorrelationId"
            FROM public.business_operation_log
            WHERE module=@Module AND aggregate_uid=@AggregateUid AND is_deleted=FALSE
            ORDER BY occurred_date, uid
            """;
        return (await Session.Connection.QueryAsync<BusinessOperationTraceItemDto>(new CommandDefinition(sql,
            new { Module = module, AggregateUid = aggregateUid }, Session.Transaction, cancellationToken: cancellationToken))).AsList();
    }

    /// <summary>查询和插入共用全库 Guid 命令锁；竞争耗尽只报告超时，不伪装成业务冲突或自动重试。</summary>
    private async Task LockCommandAsync(Guid commandId)
    {
        if (await PostgreSqlCommandLock.AcquireAsync(Session, commandId, CancellationToken.None) < 0)
            throw new TimeoutException("业务审计命令锁等待超时。");
    }
}
