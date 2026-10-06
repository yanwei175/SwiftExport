using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using SwiftExport.Core.Interfaces.Data;
using SwiftExport.Infrastructure.Data;

namespace SwiftExport.Application.Tests.Database;

/// <summary>通过无网络连接替身验证事务前租约的身份、连接周期、取消及失败清理边界。</summary>
[TestClass]
public sealed class PostgreSqlCommandLockLifecycleTests
{
    /// <summary>子命令获取响应未知时外层释放必须隔离连接，不把可能带锁的连接归还池。</summary>
    [TestMethod]
    public async Task RelatedLease_UnknownAcquisitionDiscardsConnection()
    {
        using var fixture = new Fixture(); var id = Guid.NewGuid();
        var lease = (await fixture.Acquire(id))!;
        fixture.Connection.Scalar = (_, _) => Task.FromException<object?>(new IOException("测试子命令响应丢失"));
        await Assert.ThrowsAsync<IOException>(() => PostgreSqlCommandLock.AcquireRelatedBeforeTransactionAsync(
            fixture.Session, id, [Guid.NewGuid()], default));
        await lease.DisposeAsync();
        Assert.IsTrue(fixture.Session.IsUnusable);
        Assert.AreEqual(ConnectionState.Closed, fixture.Connection.State);
        Assert.IsNull(fixture.Session.CommandLease);
    }

    /// <summary>日志提供者失效也不能阻止异常连接丢弃，或使清理异常覆盖业务结果。</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Lease_LoggingFailureStillDiscardsConnection(bool abandon)
    {
        using var fixture = new Fixture();
        var lease = (await PostgreSqlCommandLock.AcquireBeforeTransactionAsync(
            fixture.Session, Guid.NewGuid(), new FailingLogger(), default))!;
        fixture.Connection.Scalar = (_, _) => Task.FromResult<object?>(false);
        if (abandon) fixture.Session.Dispose();
        else await lease.DisposeAsync();
        Assert.IsTrue(fixture.Session.IsUnusable);
        Assert.AreEqual(ConnectionState.Closed, fixture.Connection.State);
        Assert.IsNull(fixture.Session.CommandLease);
        await lease.DisposeAsync();
    }

    /// <summary>模拟日志落盘失败，不模拟数据库或事务行为。</summary>
    private sealed class FailingLogger : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => throw new IOException("测试日志失败");
    }

    /// <summary>高隔离事务使用已确认租约，无重复 SQL；事务释放后精确解锁一次，再次释放无副作用。</summary>
    [TestMethod]
    public async Task Lease_PrecedesSnapshotAndUnlocksExactlyOnce()
    {
        using var fixture = new Fixture();
        var commandId = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
        var lease = await fixture.Acquire(commandId);
        Assert.IsNotNull(lease);
        Assert.AreEqual(1973118291719236984L, fixture.Connection.Commands[0].Key);
        Assert.IsNull(fixture.Connection.Commands[0].Transaction);
        fixture.Session.Transaction = fixture.Connection.BeginTransaction(IsolationLevel.Serializable);
        Assert.AreEqual(0, await PostgreSqlCommandLock.AcquireAsync(fixture.Session, commandId, default));
        Assert.HasCount(1, fixture.Connection.Commands);
        fixture.EndTransaction();
        await lease.DisposeAsync();
        await lease.DisposeAsync();
        Assert.HasCount(2, fixture.Connection.Commands);
        StringAssert.Contains(fixture.Connection.Commands[1].Sql, "pg_advisory_unlock");
        Assert.IsNull(fixture.Connection.Commands[1].Transaction);
        Assert.AreEqual(ConnectionState.Open, fixture.Connection.State);
        Assert.IsNull(fixture.Session.CommandLease);
    }

    /// <summary>同会话不允许嵌套、并行或不同身份借用租约；校验失败不向数据库发命令。</summary>
    [TestMethod]
    public async Task Lease_RejectsReentrancyAndOtherCommand()
    {
        using var fixture = new Fixture();
        var commandId = Guid.NewGuid();
        var lease = (await fixture.Acquire(commandId))!;
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Acquire(commandId));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Acquire(Guid.NewGuid()));
        fixture.Session.Transaction = fixture.Connection.BeginTransaction(IsolationLevel.Serializable);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PostgreSqlCommandLock.AcquireAsync(fixture.Session, Guid.NewGuid(), default));
        Assert.HasCount(1, fixture.Connection.Commands);
        fixture.EndTransaction();
        await lease.DisposeAsync();
    }

    /// <summary>事务内获取及没有事务前租约的高隔离锁继续拒绝，禁止静默降低隔离级别。</summary>
    [TestMethod]
    public async Task Lease_RejectsTransactionAndUnpreparedHighIsolation()
    {
        using var fixture = new Fixture();
        fixture.Session.Transaction = fixture.Connection.BeginTransaction(IsolationLevel.Serializable);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Acquire(Guid.NewGuid()));
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            PostgreSqlCommandLock.AcquireAsync(fixture.Session, Guid.NewGuid(), default));
        Assert.HasCount(0, fixture.Connection.Commands);
    }

    /// <summary>调用前取消不获取锁，竞争期间已确认未持锁的取消不执行多余解锁或丢弃健康连接。</summary>
    [TestMethod]
    public async Task Lease_CancellationBeforeOrBetweenAttemptsKeepsConnection()
    {
        using var fixture = new Fixture();
        await Assert.ThrowsAsync<OperationCanceledException>(() => fixture.Acquire(Guid.NewGuid(), new(true)));
        Assert.HasCount(0, fixture.Connection.Commands);
        using var cancellation = new CancellationTokenSource();
        fixture.Connection.Scalar = (_, _) =>
        {
            cancellation.Cancel();
            return Task.FromResult<object?>(false);
        };
        await Assert.ThrowsAsync<OperationCanceledException>(() => fixture.Acquire(Guid.NewGuid(), cancellation.Token));
        Assert.HasCount(1, fixture.Connection.Commands);
        Assert.IsNull(fixture.Session.CommandLease);
        Assert.AreEqual(ConnectionState.Open, fixture.Connection.State);
    }

    /// <summary>获取响应取消时仍尝试精确解锁，清理使用独立未取消令牌且保留原始取消异常。</summary>
    [TestMethod]
    public async Task Lease_UnknownAcquisitionCleansWithIndependentToken()
    {
        using var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        var failure = new OperationCanceledException(cancellation.Token);
        fixture.Connection.Scalar = (sql, cleanupToken) =>
        {
            if (sql.Contains("pg_try_advisory_lock", StringComparison.Ordinal))
            {
                cancellation.Cancel();
                return Task.FromException<object?>(failure);
            }
            Assert.IsFalse(cleanupToken.IsCancellationRequested);
            return Task.FromResult<object?>(true);
        };
        var observed = await Assert.ThrowsAsync<OperationCanceledException>(() =>
            fixture.Acquire(Guid.NewGuid(), cancellation.Token));
        Assert.AreSame(failure, observed);
        Assert.HasCount(2, fixture.Connection.Commands);
        Assert.IsNull(fixture.Session.CommandLease);
        Assert.AreEqual(ConnectionState.Open, fixture.Connection.State);
    }

    /// <summary>解锁返回否或抛出异常时隔离连接，不抛清理异常且不能再取得命令租约。</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Lease_ReleaseFailureDiscardsConnection(bool throws)
    {
        using var fixture = new Fixture();
        var lease = (await fixture.Acquire(Guid.NewGuid()))!;
        fixture.Connection.Scalar = (_, _) => throws
            ? Task.FromException<object?>(new InvalidOperationException("测试解锁失败"))
            : Task.FromResult<object?>(false);
        await lease.DisposeAsync();
        Assert.IsTrue(fixture.Session.IsUnusable);
        Assert.AreEqual(ConnectionState.Closed, fixture.Connection.State);
        Assert.IsNull(fixture.Session.CommandLease);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => fixture.Acquire(Guid.NewGuid()));
    }

    /// <summary>相同逻辑连接关闭再打开也不能继承租约或通过高隔离校验。</summary>
    [TestMethod]
    public async Task Lease_ReopenedConnectionCannotBorrowPreparation()
    {
        using var fixture = new Fixture();
        var commandId = Guid.NewGuid();
        var lease = (await fixture.Acquire(commandId))!;
        fixture.Connection.Close();
        fixture.Connection.Open();
        fixture.Session.Transaction = fixture.Connection.BeginTransaction(IsolationLevel.Serializable);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PostgreSqlCommandLock.AcquireAsync(fixture.Session, commandId, default));
        fixture.EndTransaction();
        await lease.DisposeAsync();
        Assert.IsTrue(fixture.Session.IsUnusable);
        Assert.HasCount(1, fixture.Connection.Commands);
    }

    /// <summary>事务仍存活时提前释放或遗漏租约释放均丢弃连接，避免锁提前释放后业务继续执行。</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Lease_EarlyReleaseOrScopeDisposalFailsClosed(bool disposeScope)
    {
        using var fixture = new Fixture();
        var lease = (await fixture.Acquire(Guid.NewGuid()))!;
        fixture.Session.Transaction = fixture.Connection.BeginTransaction(IsolationLevel.Serializable);
        if (disposeScope) fixture.Session.Dispose();
        else await lease.DisposeAsync();
        Assert.IsTrue(fixture.Session.IsUnusable);
        Assert.AreEqual(ConnectionState.Closed, fixture.Connection.State);
        Assert.HasCount(1, fixture.Connection.Commands);
        await lease.DisposeAsync();
    }

    /// <summary>构造真实会话与无网络连接，不读取配置或连接数据库。</summary>
    private sealed class Fixture : IDisposable
    {
        internal readonly Connection Connection = new();
        internal readonly DbSession Session;
        internal Fixture() => Session = new(new Factory(Connection));
        internal Task<IAsyncDisposable?> Acquire(Guid id, CancellationToken token = default) =>
            PostgreSqlCommandLock.AcquireBeforeTransactionAsync(Session, id, NullLogger.Instance, token);
        internal void EndTransaction()
        {
            Session.Transaction!.Dispose();
            Session.Transaction = null;
        }
        public void Dispose() => Session.Dispose();
    }

    /// <summary>提供单一测试连接。</summary>
    private sealed class Factory(Connection connection) : IDbConnectionFactory
    {
        public IDbConnection CreateConnection() => connection;
    }

    /// <summary>模拟连接开闭、事务身份和异步标量结果，以验证真实 Dapper 调用及取消令牌。</summary>
    private sealed class Connection : DbConnection
    {
        private ConnectionState _state;
        internal readonly List<(string Sql, long Key, DbTransaction? Transaction)> Commands = [];
        internal Func<string, CancellationToken, Task<object?>> Scalar = (_, _) => Task.FromResult<object?>(true);
        [AllowNull] public override string ConnectionString { get; set; } = "";
        public override string Database => "no-network";
        public override string DataSource => "none";
        public override string ServerVersion => "test";
        public override ConnectionState State => _state;
        public override void Open() => ChangeState(ConnectionState.Open);
        public override void Close() => ChangeState(ConnectionState.Closed);
        public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => new Transaction(this, isolationLevel);
        protected override DbCommand CreateDbCommand() => new Command(this);
        protected override void Dispose(bool disposing) { if (disposing) Close(); base.Dispose(disposing); }

        /// <summary>只有真实开闭变化才产生事件，用于模拟同逻辑连接的新周期。</summary>
        private void ChangeState(ConnectionState state)
        {
            var previous = _state;
            _state = state;
            if (previous != state) OnStateChange(new(previous, state));
        }
    }

    /// <summary>保留隔离级别与连接身份，不执行数据库提交。</summary>
    private sealed class Transaction(Connection connection, IsolationLevel isolation) : DbTransaction
    {
        public override IsolationLevel IsolationLevel => isolation;
        protected override DbConnection DbConnection => connection;
        public override void Commit() { }
        public override void Rollback() { }
    }

    /// <summary>记录 SQL、锁键及事务，并将异步执行委托给测试场景。</summary>
    private sealed class Command(Connection connection) : DbCommand
    {
        private readonly NpgsqlCommand _parameters = new();
        [AllowNull] public override string CommandText { get; set; } = "";
        public override int CommandTimeout { get; set; }
        public override CommandType CommandType { get; set; }
        public override bool DesignTimeVisible { get; set; }
        public override UpdateRowSource UpdatedRowSource { get; set; }
        protected override DbConnection? DbConnection { get; set; } = connection;
        protected override DbTransaction? DbTransaction { get; set; }
        protected override DbParameterCollection DbParameterCollection => _parameters.Parameters;
        protected override DbParameter CreateDbParameter() => new NpgsqlParameter();
        public override void Cancel() { }
        public override void Prepare() { }
        public override int ExecuteNonQuery() => throw new NotSupportedException();
        public override object? ExecuteScalar() => ExecuteScalarAsync(default).GetAwaiter().GetResult();
        public override Task<object?> ExecuteScalarAsync(CancellationToken token)
        {
            connection.Commands.Add((CommandText, (long)_parameters.Parameters["Key"].Value!, DbTransaction));
            return connection.Scalar(CommandText, token);
        }
        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) _parameters.Dispose(); base.Dispose(disposing); }
    }
}
