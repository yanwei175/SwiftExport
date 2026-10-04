using System.Data;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using SwiftExport.Core.Entities.Auditing;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.Repositories.Auditing;
using SwiftExport.Infrastructure.Services.Auth;

namespace SwiftExport.Application.Tests.Database;

/// <summary>真实 PG 审计仓储验收；所有测试只写随机身份并最终回滚，不提交或删除不可变审计日志，不替代 API 验收。</summary>
[TestClass]
[TestCategory("PostgreSqlIntegration")]
public sealed class PostgreSqlBusinessOperationLogIntegrationTests
{
    /// <summary>同事务追加、查询和追溯保留身份、JSON 和时间；另一连接不可见，回滚后明确身份无残留。</summary>
    [TestMethod]
    [Timeout(60000)]
    public async Task AuditLog_RealProjectionAndRollback()
    {
        var connectionString = PostgreSqlIntegrationEnvironment.GetConnectionString();
        var log = NewLog();
        using var session = Open(connectionString);
        using var other = Open(connectionString);
        using var transaction = session.Connection.BeginTransaction(IsolationLevel.ReadCommitted);
        session.Transaction = transaction;
        try
        {
            await AssertAbsent(other, log.CommandId);
            var repository = Repository(session);
            Assert.AreEqual(1, await repository.InsertAsync(log));
            var second = NewLog();
            second.CommandId = log.CommandId;
            second.Module = log.Module;
            second.AggregateUid = log.AggregateUid;
            second.OperationType = "Reverse";
            second.OccurredDate = log.OccurredDate.AddSeconds(1);
            Assert.AreEqual(1, await repository.InsertAsync(second));
            var prior = await repository.GetByModuleCommandAsync(log.Module, log.CommandId);
            Assert.IsNotNull(prior);
            Assert.AreEqual(log.Uid, prior.Uid);
            Assert.AreEqual(1, prior.Version);
            Assert.AreEqual(log.UserUid, prior.UserUid);
            Assert.AreEqual(log.ChangedFieldsJson, prior.ChangedFieldsJson);
            Assert.AreEqual(log.RelatedObjectsJson, prior.RelatedObjectsJson);
            Assert.AreEqual("__pgtest_audit", prior.CreatedBy);
            Assert.AreEqual(DateTimeKind.Unspecified, prior.OccurredDate.Kind);
            Assert.IsTrue(Math.Abs((prior.OccurredDate - log.OccurredDate).Ticks) <= 10, "目标 timestamp(6) 只允许微秒精度差异。");
            var byOperation = await repository.GetByCommandAsync(log.Module, "Reverse", log.CommandId);
            Assert.AreEqual(second.Uid, byOperation!.Uid);
            Assert.IsNull(await repository.GetByCommandAsync(log.Module, "Missing", log.CommandId));
            var trace = await repository.GetByAggregateAsync(log.Module, log.AggregateUid);
            CollectionAssert.AreEqual(new[] { log.Uid, second.Uid }, trace.Select(t => t.Uid).ToArray());
            Assert.AreEqual(log.UserNameSnapshot, trace[0].UserName);
            Assert.IsNull(trace[0].Reason);
            Assert.AreEqual(log.ChangedFieldsJson, trace[0].ChangedFieldsJson);
            await AssertAbsent(other, log.CommandId);
        }
        finally
        {
            transaction.Rollback();
            session.Transaction = null;
            await AssertAbsent(other, log.CommandId);
        }
    }

    /// <summary>三元唯一键和 JSON 约束真实拒绝无效插入；不可变触发器拒绝明确 Uid 更新/删除，通过保存点恢复后整事务回滚。</summary>
    [TestMethod]
    [Timeout(60000)]
    public async Task AuditLog_UniqueJsonAndAppendOnlyConstraints()
    {
        var connectionString = PostgreSqlIntegrationEnvironment.GetConnectionString();
        var log = NewLog();
        var invalid = NewLog();
        using var session = Open(connectionString);
        using var transaction = session.Connection.BeginTransaction(IsolationLevel.ReadCommitted);
        session.Transaction = transaction;
        try
        {
            await AssertAbsent(session, log.CommandId);
            await AssertAbsent(session, invalid.CommandId);
            var repository = Repository(session);
            Assert.AreEqual(1, await repository.InsertAsync(log));
            await Sql(session, "SAVEPOINT audit_check");
            var duplicate = NewLog();
            duplicate.Module = log.Module;
            duplicate.OperationType = log.OperationType;
            duplicate.CommandId = log.CommandId;
            var unique = await Assert.ThrowsExactlyAsync<PostgresException>(() => repository.InsertAsync(duplicate));
            Assert.AreEqual("23505", unique.SqlState);
            await Sql(session, "ROLLBACK TO SAVEPOINT audit_check");
            invalid.ChangedFieldsJson = "not-json";
            var json = await Assert.ThrowsExactlyAsync<PostgresException>(() => repository.InsertAsync(invalid));
            Assert.AreEqual("23514", json.SqlState);
            await Sql(session, "ROLLBACK TO SAVEPOINT audit_check");
            foreach (var sql in new[]
            {
                "UPDATE public.business_operation_log SET summary='禁止改写' WHERE uid=@Uid",
                "DELETE FROM public.business_operation_log WHERE uid=@Uid"
            })
            {
                var immutable = await Assert.ThrowsExactlyAsync<PostgresException>(() => session.Connection.ExecuteAsync(
                    new CommandDefinition(sql, new { log.Uid }, transaction, commandTimeout: 10)));
                Assert.AreEqual("P3100", immutable.SqlState);
                await Sql(session, "ROLLBACK TO SAVEPOINT audit_check");
            }
            Assert.AreEqual(log.Uid, (await repository.GetByCommandAsync(log.Module, log.OperationType, log.CommandId))!.Uid);
        }
        finally
        {
            transaction.Rollback();
            session.Transaction = null;
            using var verification = Open(connectionString);
            await AssertAbsent(verification, log.CommandId);
            await AssertAbsent(verification, invalid.CommandId);
        }
    }

    /// <summary>缺失命令身份先由首连接占锁，第二连接的两个查询和插入均竞争超时；释放后插入成功，全部日志仍回滚。</summary>
    [TestMethod]
    [Timeout(90000)]
    public async Task AuditLog_MissingCommandLockProtectsQueriesAndInsert()
    {
        var connectionString = PostgreSqlIntegrationEnvironment.GetConnectionString();
        var log = NewLog();
        using var first = Open(connectionString);
        using var second = Open(connectionString);
        using var firstTransaction = first.Connection.BeginTransaction(IsolationLevel.ReadCommitted);
        using var secondTransaction = second.Connection.BeginTransaction(IsolationLevel.ReadCommitted);
        first.Transaction = firstTransaction;
        second.Transaction = secondTransaction;
        var released = false;
        try
        {
            await AssertAbsent(first, log.CommandId);
            var owner = Repository(first);
            var competitor = Repository(second);
            Assert.IsNull(await owner.GetByModuleCommandAsync(log.Module, log.CommandId));
            // 首次查询已经真实返回空并持有事务命令锁；逐次等待十秒超时证明竞争，不用 sleep 推测启动时序。
            await Assert.ThrowsExactlyAsync<TimeoutException>(() => competitor.GetByModuleCommandAsync("__pgtest_other_module", log.CommandId));
            await Assert.ThrowsExactlyAsync<TimeoutException>(() => competitor.GetByCommandAsync(log.Module, "DifferentOperation", log.CommandId));
            await Assert.ThrowsExactlyAsync<TimeoutException>(() => competitor.InsertAsync(log));
            Assert.IsNull(log.CreatedDate, "锁失败必须在审计打戳和插入之前拒绝。");
            firstTransaction.Rollback();
            first.Transaction = null;
            released = true;
            Assert.AreEqual(1, await competitor.InsertAsync(log));
            Assert.AreEqual(log.Uid, (await competitor.GetByModuleCommandAsync(log.Module, log.CommandId))!.Uid);
            await AssertAbsent(first, log.CommandId);
        }
        finally
        {
            if (!released) firstTransaction.Rollback();
            first.Transaction = null;
            secondTransaction.Rollback();
            second.Transaction = null;
            using var verification = Open(connectionString);
            await AssertAbsent(verification, log.CommandId);
        }
    }

    /// <summary>RR 和 Serializable 在取得命令锁或写入前拒绝，保持明确切库门槛；普通追溯不受该写入限制。</summary>
    [TestMethod]
    [Timeout(60000)]
    public async Task AuditLog_HigherIsolationRejectedBeforeWrite()
    {
        var connectionString = PostgreSqlIntegrationEnvironment.GetConnectionString();
        var log = NewLog();
        using var session = Open(connectionString);
        foreach (var isolation in new[] { IsolationLevel.RepeatableRead, IsolationLevel.Serializable })
        {
            using var transaction = session.Connection.BeginTransaction(isolation);
            session.Transaction = transaction;
            try
            {
                var repository = Repository(session);
                await Assert.ThrowsExactlyAsync<NotSupportedException>(() => repository.GetByModuleCommandAsync(log.Module, log.CommandId));
                await Assert.ThrowsExactlyAsync<NotSupportedException>(() => repository.GetByCommandAsync(log.Module, log.OperationType, log.CommandId));
                await Assert.ThrowsExactlyAsync<NotSupportedException>(() => repository.InsertAsync(log));
                Assert.IsNull(log.CreatedDate);
                Assert.HasCount(0, await repository.GetByAggregateAsync(log.Module, log.AggregateUid));
            }
            finally { transaction.Rollback(); session.Transaction = null; }
        }
        await AssertAbsent(session, log.CommandId);
    }

    /// <summary>使用主控校验后的连接材料创建真实会话，不记录连接信息。</summary>
    private static DbSession Open(string connectionString) => new(new PostgreSqlConnectionFactory(connectionString));

    /// <summary>为审计仓储复用同一会话事务及最小测试身份。</summary>
    private static PostgreSqlBusinessOperationLogRepository Repository(DbSession session)
    {
        var user = new AuditUser();
        return new(new RepositoryBaseContext(session, NullLoggerFactory.Instance, new AuditStampService(user), user));
    }

    /// <summary>生成随机日志、命令和聚合身份，模块使用测试前缀；原始 JSON 刻意保留格式以核验旧回执字符串。</summary>
    private static BusinessOperationLog NewLog() => new()
    {
        Uid = Guid.NewGuid(), CommandId = Guid.NewGuid(), AggregateUid = Guid.NewGuid(), Module = "__pgtest_" + Guid.NewGuid().ToString("N"),
        OperationType = "Post", OperationName = "测试审计", AggregateType = "__pgtest_aggregate", CorrelationId = Guid.NewGuid().ToString("N"),
        UserNameSnapshot = "__pgtest_audit", OccurredDate = DateTime.Now,
        ChangedFieldsJson = "{ \"Fingerprint\" : \"original\" }", RelatedObjectsJson = "[ {\"Uid\":1} ]"
    };

    /// <summary>检查本次随机命令无可见残留；查询保留当前事务，只针对明确 CommandId。</summary>
    private static async Task AssertAbsent(DbSession session, Guid commandId) => Assert.AreEqual(0L,
        await session.Connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT count(*) FROM public.business_operation_log WHERE command_id=@CommandId", new { CommandId = commandId }, session.Transaction, commandTimeout: 10)));

    /// <summary>仅执行本文件固定的保存点操作，失败恢复不关闭不可变防线。</summary>
    private static Task<int> Sql(DbSession session, string sql) => session.Connection.ExecuteAsync(new CommandDefinition(sql, transaction: session.Transaction, commandTimeout: 10));

    /// <summary>只提供审计名称，不创建账号或授予权限。</summary>
    private sealed class AuditUser : ICurrentUser
    {
        /// <summary>测试身份的审计名称。</summary>
        public string UserName => "__pgtest_audit";
        /// <summary>测试不持有真实账号身份。</summary>
        public Guid? UserId => null;
        /// <summary>仅满足审计服务上下文，不能代表 API 认证。</summary>
        public bool IsAuthenticated => true;
    }
}
