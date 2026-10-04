using System.Data;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Data;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.Repositories.Systems.Numbering;
using SwiftExport.Infrastructure.Services.Auth;

namespace SwiftExport.Application.Tests.Database;

/// <summary>在获批故障专用库验证真实锁清理、连接丢失和 Serializable 冲突；只作用于本例连接及随机序列行。</summary>
[TestClass]
[TestCategory("PostgreSqlFailureIntegration")]
public sealed class PostgreSqlCommandFailureIntegrationTests
{
    /// <summary>精确提前解除本例锁，使租约清理返回 false；原物理连接必须丢弃，同一身份可由新连接重新获取。</summary>
    [TestMethod]
    [Timeout(60000)]
    public async Task Lease_UnlockFalseDiscardsPhysicalConnection()
    {
        var connection = PostgreSqlIntegrationEnvironment.GetFailureConnectionString();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        var token = timeout.Token;
        var pooledConnection = IsolatedPool(connection);
        using var observer = Open(IsolatedPool(connection));
        using var failed = Open(pooledConnection);
        var pid = ((NpgsqlConnection)failed.Connection).ProcessID;
        var commandId = Guid.NewGuid();
        await using var lease = await AcquireAsync(failed, commandId, token);
        await AssertBackendHoldsLockAsync(observer, pid, token);
        Assert.IsTrue(await failed.Connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT pg_advisory_unlock(@Key)", new { lease.Key }, cancellationToken: token)));
        await lease.DisposeAsync();
        await AssertDiscardedAndReacquireAsync(failed, observer, pooledConnection, pid, commandId, token);
    }

    /// <summary>仅终止调用 SQL 的本例后端，验证真实连接丢失后清理隔离会话且释放命令身份。</summary>
    [TestMethod]
    [Timeout(60000)]
    public async Task Lease_SelfTerminatedBackendIsDiscardedAndIdentityReleased()
    {
        var connection = PostgreSqlIntegrationEnvironment.GetFailureConnectionString();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        var token = timeout.Token;
        var pooledConnection = IsolatedPool(connection);
        using var observer = Open(IsolatedPool(connection));
        using var failed = Open(pooledConnection);
        var pid = ((NpgsqlConnection)failed.Connection).ProcessID;
        var commandId = Guid.NewGuid();
        await using var lease = await AcquireAsync(failed, commandId, token);
        await AssertBackendHoldsLockAsync(observer, pid, token);
        var error = await Assert.ThrowsAsync<NpgsqlException>(async () =>
        {
            // 不接受外部 PID，数据库函数只能指向执行这条 SQL 的本连接。
            await failed.Connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                "SELECT pg_terminate_backend(pg_backend_pid())", cancellationToken: token));
            // 某些驱动时序可能先收到函数结果，下一条请求仍必须确认真实连接已经丢失。
            await failed.Connection.ExecuteScalarAsync<int>(new CommandDefinition("SELECT 1", cancellationToken: token));
        });
        if (error is PostgresException postgres)
            Assert.AreEqual("57P01", postgres.SqlState);
        await lease.DisposeAsync();
        await AssertDiscardedAndReacquireAsync(failed, observer, pooledConnection, pid, commandId, token);
    }

    /// <summary>两个 Serializable 事务读取同一既有序列后形成 40001，失败事务回滚且由新事务显式继续；不自动重试。</summary>
    [TestMethod]
    [Timeout(60000)]
    public async Task Serializable_StaleSequenceUpdateFailsAndRollsBack()
    {
        var connection = PostgreSqlIntegrationEnvironment.GetFailureConnectionString();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        var token = timeout.Token;
        var docType = "__pgfail_" + Guid.NewGuid().ToString("N")[..12];
        var parameters = new { DocType = docType };
        const string readSql = "SELECT current_no FROM public.document_sequence WHERE doc_type=@DocType AND year_month='202610'";
        const string updateSql = "UPDATE public.document_sequence SET current_no=current_no+1 WHERE doc_type=@DocType AND year_month='202610'";
        using (var seed = Open(IsolatedPool(connection)))
        {
            var user = new TestUser();
            var repository = new PostgreSqlDocumentSequenceRepository(new RepositoryBaseContext(seed,
                NullLoggerFactory.Instance, new AuditStampService(user), user));
            Assert.AreEqual(1, (await repository.GetNextAsync(docType, "202610", user.UserName)).CurrentNo);
        }
        using var first = Open(IsolatedPool(connection));
        using var stale = Open(IsolatedPool(connection));
        using var firstTransaction = first.Connection.BeginTransaction(IsolationLevel.Serializable);
        using var staleTransaction = stale.Connection.BeginTransaction(IsolationLevel.Serializable);
        first.Transaction = firstTransaction;
        stale.Transaction = staleTransaction;
        var firstCommitted = false;
        var staleRolledBack = false;
        try
        {
            // 两次读取均完成后才更新，无并发睡眠或依赖调度器建立快照。
            Assert.AreEqual(1, await first.Connection.ExecuteScalarAsync<int>(new CommandDefinition(
                readSql, parameters, firstTransaction, cancellationToken: token)));
            Assert.AreEqual(1, await stale.Connection.ExecuteScalarAsync<int>(new CommandDefinition(
                readSql, parameters, staleTransaction, cancellationToken: token)));
            Assert.AreEqual(1, await first.Connection.ExecuteAsync(new CommandDefinition(
                updateSql, parameters, firstTransaction, cancellationToken: token)));
            firstTransaction.Commit();
            firstCommitted = true;
            first.Transaction = null;
            var error = await Assert.ThrowsExactlyAsync<PostgresException>(async () =>
            {
                await stale.Connection.ExecuteAsync(new CommandDefinition(
                    updateSql, parameters, staleTransaction, cancellationToken: token));
            });
            Assert.AreEqual("40001", error.SqlState);
            Assert.AreEqual(DatabaseFailureKind.SerializationFailure, stale.Errors.Classify(error));
            staleTransaction.Rollback();
            staleRolledBack = true;
            stale.Transaction = null;
            using var next = Open(IsolatedPool(connection));
            Assert.AreEqual(2, await next.Connection.ExecuteScalarAsync<int>(new CommandDefinition(
                readSql, parameters, cancellationToken: token)));
            using var nextTransaction = next.Connection.BeginTransaction(IsolationLevel.Serializable);
            next.Transaction = nextTransaction;
            try
            {
                Assert.AreEqual(1, await next.Connection.ExecuteAsync(new CommandDefinition(
                    updateSql, parameters, nextTransaction, cancellationToken: token)));
                nextTransaction.Commit();
            }
            finally { next.Transaction = null; }
            using var observer = Open(IsolatedPool(connection));
            Assert.AreEqual(3, await observer.Connection.ExecuteScalarAsync<int>(new CommandDefinition(
                readSql, parameters, cancellationToken: token)));
            Assert.IsNull(first.Transaction);
            Assert.IsNull(stale.Transaction);
            Assert.IsNull(next.Transaction);
        }
        finally
        {
            // 失败断言或取消时仍让最外层释放自身事务，不清理已提交的随机测试行。
            try { if (!firstCommitted && firstTransaction.Connection is not null) firstTransaction.Rollback(); }
            finally
            {
                first.Transaction = null;
                try { if (!staleRolledBack && staleTransaction.Connection is not null) staleTransaction.Rollback(); }
                finally { stale.Transaction = null; }
            }
        }
    }

    /// <summary>仅在本例事务设置服务端短超时，57014 保留服务端取消分类且不冒充用户令牌取消。</summary>
    [TestMethod]
    [Timeout(60000)]
    public async Task StatementTimeout_IsServerQueryCanceledAndTransactionRollsBack()
    {
        var connection = PostgreSqlIntegrationEnvironment.GetFailureConnectionString();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        using var session = Open(IsolatedPool(connection));
        using var transaction = session.Connection.BeginTransaction(IsolationLevel.ReadCommitted);
        session.Transaction = transaction;
        try
        {
            await session.Connection.ExecuteAsync(new CommandDefinition(
                "SET LOCAL statement_timeout = '100ms'", transaction: transaction, cancellationToken: timeout.Token));
            var error = await Assert.ThrowsExactlyAsync<PostgresException>(async () =>
            {
                await session.Connection.ExecuteAsync(new CommandDefinition(
                    "SELECT pg_sleep(5)", transaction: transaction, commandTimeout: 10, cancellationToken: timeout.Token));
            });
            Assert.AreEqual("57014", error.SqlState);
            Assert.AreEqual(DatabaseFailureKind.ServerQueryCanceled, session.Errors.Classify(error));
            Assert.IsFalse(timeout.IsCancellationRequested, "服务端超时不能伪装为调用方取消。");
        }
        finally
        {
            try { transaction.Rollback(); }
            finally { session.Transaction = null; }
        }
        Assert.AreEqual(1, await session.Connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT 1", cancellationToken: timeout.Token)));
        Assert.IsNull(session.Transaction);
    }

    /// <summary>为本例分配独立且最多一条物理连接的池，防止故障清池影响其他测试连接。</summary>
    private static string IsolatedPool(string connection)
    {
        var settings = new NpgsqlConnectionStringBuilder(connection)
        {
            ApplicationName = "__pgfail_" + Guid.NewGuid().ToString("N"), Pooling = true,
            MinPoolSize = 0, MaxPoolSize = 1, Multiplexing = false, NoResetOnClose = false,
            Timeout = 10, CommandTimeout = 10
        };
        return settings.ConnectionString;
    }

    /// <summary>以现有工厂创建共享会话，不输出连接串或建立任何数据库结构。</summary>
    private static DbSession Open(string connection) => new(new PostgreSqlConnectionFactory(connection));

    /// <summary>取得真实租约并保留其精确 Key，不在测试中复制命令锁哈希算法。</summary>
    private static async Task<PostgreSqlCommandLock.Lease> AcquireAsync(DbSession session, Guid commandId, CancellationToken token)
    {
        var lease = await PostgreSqlCommandLock.AcquireBeforeTransactionAsync(session, commandId, NullLogger.Instance, token);
        Assert.IsNotNull(lease);
        Assert.IsInstanceOfType<PostgreSqlCommandLock.Lease>(lease);
        return (PostgreSqlCommandLock.Lease)lease;
    }

    /// <summary>从另一物理连接确认本例后端已经持有 advisory lock，避免空锁清理造成假阳性。</summary>
    private static async Task AssertBackendHoldsLockAsync(DbSession observer, int pid, CancellationToken token)
    {
        Assert.IsTrue(await observer.Connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT count(*) FROM pg_locks WHERE pid=@Pid AND locktype='advisory' AND granted",
            new { Pid = pid }, cancellationToken: token)) > 0);
    }

    /// <summary>验证逻辑隔离、原后端消失与无锁，再从相同连接池重新取得身份；观察等待仅用于后端退出且有独立十秒预算。</summary>
    private static async Task AssertDiscardedAndReacquireAsync(DbSession failed, DbSession observer,
        string pooledConnection, int pid, Guid commandId, CancellationToken token)
    {
        Assert.IsTrue(failed.IsUnusable);
        Assert.AreEqual(ConnectionState.Closed, failed.Connection.State);
        Assert.IsNull(failed.CommandLease);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        while (await observer.Connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT count(*) FROM pg_stat_activity WHERE pid=@Pid", new { Pid = pid }, cancellationToken: deadline.Token)) != 0)
            await Task.Delay(50, deadline.Token);
        Assert.AreEqual(0L, await observer.Connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT count(*) FROM pg_locks WHERE pid=@Pid AND locktype='advisory'", new { Pid = pid }, cancellationToken: token)));
        using var replacement = Open(pooledConnection);
        Assert.AreNotEqual(pid, ((NpgsqlConnection)replacement.Connection).ProcessID, "被隔离的物理连接不能从同一池重新借出。");
        await using var lease = await AcquireAsync(replacement, commandId, token);
        Assert.IsTrue(lease.Held);
        await lease.DisposeAsync();
        Assert.IsFalse(replacement.IsUnusable);
        Assert.IsNull(replacement.CommandLease);
        Assert.AreEqual(ConnectionState.Open, replacement.Connection.State);
    }

    /// <summary>提供独立取号审计身份，不创建账户。</summary>
    private sealed class TestUser : ICurrentUser
    {
        public Guid? UserId { get; } = Guid.NewGuid();
        public string UserName => "__pgfail_sequence";
        public bool IsAuthenticated => true;
    }
}
