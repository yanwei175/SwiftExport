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

/// <summary>在专用测试库验证审计仓储自身的事务前租约；随机日志全部回滚，不修改既有结构和数据。</summary>
[TestClass]
[DoNotParallelize]
[TestCategory("PostgreSqlAuditPreparationIntegration")]
public sealed class PostgreSqlAuditPreparationIntegrationTests
{
    /// <summary>准备租约后高隔离可追加并读回原始审计，回滚后日志消失，物理连接上的会话锁释放。</summary>
    [TestMethod]
    [DataRow(IsolationLevel.RepeatableRead)]
    [DataRow(IsolationLevel.Serializable)]
    public async Task PreparedHighIsolation_WritesRollsBackAndUnlocks(IsolationLevel isolation)
    {
        using var session = Open();
        var repository = Repository(session);
        var log = NewLog();
        var lease = await repository.AcquireBeforeTransactionAsync(log.CommandId, default);
        Assert.IsNotNull(lease);
        try
        {
            using (var transaction = session.Connection.BeginTransaction(isolation))
            {
                session.Transaction = transaction;
                try
                {
                    Assert.AreEqual(1, await repository.InsertAsync(log));
                    var stored = await repository.GetByCommandAsync(log.Module, log.OperationType, log.CommandId);
                    Assert.IsNotNull(stored);
                    Assert.AreEqual(log.Uid, stored.Uid);
                    Assert.AreEqual(log.ChangedFieldsJson, stored.ChangedFieldsJson);
                    transaction.Rollback();
                }
                finally { session.Transaction = null; }
            }
        }
        finally { await lease.DisposeAsync(); }
        Assert.AreEqual(0L, await session.Connection.ExecuteScalarAsync<long>(
            "SELECT count(*) FROM public.business_operation_log WHERE command_id=@CommandId",
            new { log.CommandId }));
        await AssertUnlocked(session);
        // 同 scope 可再次准备，证明租约占位也已清理。
        var next = await repository.AcquireBeforeTransactionAsync(Guid.NewGuid(), default);
        Assert.IsNotNull(next);
        await next.DisposeAsync();
        await AssertUnlocked(session);
    }

    /// <summary>准备身份与写入身份不一致时在打戳和 SQL 前拒绝，不借用其他命令租约。</summary>
    [TestMethod]
    public async Task DifferentCommand_IsRejectedBeforeAuditWrite()
    {
        using var session = Open();
        var repository = Repository(session);
        var log = NewLog();
        var lease = await repository.AcquireBeforeTransactionAsync(Guid.NewGuid(), default);
        Assert.IsNotNull(lease);
        try
        {
            using var transaction = session.Connection.BeginTransaction(IsolationLevel.Serializable);
            session.Transaction = transaction;
            try
            {
                await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => repository.InsertAsync(log));
                Assert.IsNull(log.CreatedDate);
                transaction.Rollback();
            }
            finally { session.Transaction = null; }
        }
        finally { await lease.DisposeAsync(); }
        await AssertUnlocked(session);
    }

    /// <summary>没有事务前租约的高隔离写入仍拒绝；新增可选能力不放宽旧锁防线。</summary>
    [TestMethod]
    [DataRow(IsolationLevel.RepeatableRead)]
    [DataRow(IsolationLevel.Serializable)]
    public async Task UnpreparedHighIsolation_RemainsRejected(IsolationLevel isolation)
    {
        using var session = Open();
        var repository = Repository(session);
        var log = NewLog();
        using var transaction = session.Connection.BeginTransaction(isolation);
        session.Transaction = transaction;
        try
        {
            await Assert.ThrowsExactlyAsync<NotSupportedException>(() => repository.InsertAsync(log));
            Assert.IsNull(log.CreatedDate);
            transaction.Rollback();
        }
        finally { session.Transaction = null; }
        await AssertUnlocked(session);
    }

    /// <summary>只允许已登记的 failure 测试库；环境校验失败不会连接其他数据库。</summary>
    private static DbSession Open() => new(new PostgreSqlConnectionFactory(
        PostgreSqlIntegrationEnvironment.GetFailureConnectionString()));

    /// <summary>使用与仓储相同的 scope 打审计戳，不授予业务权限。</summary>
    private static PostgreSqlBusinessOperationLogRepository Repository(DbSession session)
    {
        var user = new AuditUser();
        return new(new RepositoryBaseContext(session, NullLoggerFactory.Instance, new AuditStampService(user), user));
    }

    /// <summary>生成本测试独有的命令和聚合身份，保留带空白的原始 JSON。</summary>
    private static BusinessOperationLog NewLog() => new()
    {
        CommandId = Guid.NewGuid(), AggregateUid = Guid.NewGuid(), Module = "__pgtest_audit_prepare",
        AggregateType = "SysRole", OperationType = "CreateRole", OperationName = "事务前审计准备实测",
        CorrelationId = Guid.NewGuid().ToString("N"), OccurredDate = DateTime.Now,
        UserNameSnapshot = "__pgtest_audit_prepare", ChangedFieldsJson = "{ \"Before\" : null, \"After\" : {} }"
    };

    /// <summary>按当前物理连接查询实际 advisory 锁，不以逻辑租约状态代替数据库释放证据。</summary>
    private static async Task AssertUnlocked(DbSession session) => Assert.AreEqual(0L,
        await session.Connection.ExecuteScalarAsync<long>(
            "SELECT count(*) FROM pg_locks WHERE locktype='advisory' AND pid=@Pid",
            new { Pid = ((NpgsqlConnection)session.Connection).ProcessID }));

    /// <summary>仅提供审计名称，无真实用户身份。</summary>
    private sealed class AuditUser : ICurrentUser
    {
        /// <summary>测试审计名称。</summary>
        public string UserName => "__pgtest_audit_prepare";
        /// <summary>不借用已有账号。</summary>
        public Guid? UserId => null;
        /// <summary>仅满足审计上下文。</summary>
        public bool IsAuthenticated => true;
    }
}
