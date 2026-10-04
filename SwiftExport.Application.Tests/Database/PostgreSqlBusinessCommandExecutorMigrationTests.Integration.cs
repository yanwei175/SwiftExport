using System.Data;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Commands;
using SwiftExport.Application.Contexts;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Core.Entities.Auditing;
using SwiftExport.Core.Entities.Commands;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Commands;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.Repositories.Auditing;
using SwiftExport.Infrastructure.Repositories.Commands;
using SwiftExport.Infrastructure.Repositories.Systems.Numbering;
using SwiftExport.Infrastructure.Services.Auth;

namespace SwiftExport.Application.Tests.Database;

/// <summary>在本批专用库验证真实命令执行器的 Serializable 提交、重放、回滚和快照前互斥；成功数据保留供用户核验。</summary>
[TestClass]
[TestCategory("PostgreSqlIntegration")]
public sealed class PostgreSqlBusinessCommandExecutorMigrationIntegrationTests
{
    /// <summary>首次提交后换连接重放原始结果，取号、回执和审计只形成一次。</summary>
    [TestMethod]
    [Timeout(60000)]
    public async Task Serializable_CommittedCommandReplaysOnNewConnection()
    {
        var connection = PostgreSqlIntegrationEnvironment.GetCommandConnectionString();
        var keys = new TestKeys();
        var user = new TestUser(Guid.NewGuid());
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        var calls = 0;
        CommandExecutionResult<TestResult> first;
        using (var writer = new Fixture(connection, keys, user))
        {
            first = await writer.ExecuteAsync(async token =>
            {
                calls++;
                return await writer.WriteFactsAsync(token);
            }, timeout.Token);
            Assert.IsFalse(first.IsReplay);
            Assert.IsNull(writer.Session.Transaction);
        }
        using var replay = new Fixture(connection, keys, user);
        var snapshot = await replay.SnapshotAsync(timeout.Token);
        var second = await replay.ExecuteAsync(async token =>
        {
            calls++;
            return await replay.WriteFactsAsync(token);
        }, timeout.Token);
        Assert.IsTrue(second.IsReplay);
        Assert.AreEqual(first.Value, second.Value);
        Assert.AreEqual(1, calls);
        Assert.AreEqual(snapshot, await replay.SnapshotAsync(timeout.Token));
        await replay.AssertFactsAsync(1, timeout.Token);
        Assert.IsNull(replay.Session.Transaction);
    }

    /// <summary>同一已提交身份在操作者、内容、操作或目标变化时拒绝执行，完整持久化快照保持不变。</summary>
    [TestMethod]
    [DataRow("actor")]
    [DataRow("input")]
    [DataRow("operation")]
    [DataRow("target")]
    [Timeout(60000)]
    public async Task Serializable_ConflictingIdentityPreservesCommittedFacts(string difference)
    {
        var connection = PostgreSqlIntegrationEnvironment.GetCommandConnectionString();
        var keys = new TestKeys();
        var user = new TestUser(Guid.NewGuid());
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        var calls = 0;
        using var writer = new Fixture(connection, keys, user);
        await writer.ExecuteAsync(async token => { calls++; return await writer.WriteFactsAsync(token); }, timeout.Token);
        using var challenger = new Fixture(connection, keys,
            difference == "actor" ? new TestUser(Guid.NewGuid()) : user);
        var snapshot = await challenger.SnapshotAsync(timeout.Token);
        var error = await Assert.ThrowsExactlyAsync<BusinessCommandException>(() => challenger.Executor.ExecuteAsync(
            keys.CommandId, difference == "operation" ? "__pgtest_other" : Fixture.CommandType,
            difference == "target" ? Guid.NewGuid() : keys.TargetUid,
            new { Amount = difference == "input" ? 2m : 1m },
            async token => { calls++; return await challenger.WriteFactsAsync(token); }, timeout.Token,
            IsolationLevel.Serializable));
        Assert.AreEqual(ServiceErrorCodes.CommandConflict, error.Code);
        Assert.AreEqual(1, calls);
        Assert.IsNull(challenger.Session.Transaction);
        Assert.AreEqual(snapshot, await challenger.SnapshotAsync(timeout.Token));
        await challenger.AssertFactsAsync(1, timeout.Token);
    }

    /// <summary>业务写入或真实回执追加后失败与取消均回滚三个事实，原连接保持打开时另一连接仍能取得同一身份租约并提交。</summary>
    [TestMethod]
    [DataRow("action-failure")]
    [DataRow("receipt-failure")]
    [DataRow("action-cancel")]
    [DataRow("receipt-cancel")]
    [Timeout(60000)]
    public async Task Serializable_FailureOrCancellationRollsBackAndReleasesLease(string stage)
    {
        var connection = PostgreSqlIntegrationEnvironment.GetCommandConnectionString();
        var keys = new TestKeys();
        var user = new TestUser(Guid.NewGuid());
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        using var canceled = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        using var failed = new Fixture(connection, keys, user, receipt => new ReceiptProbe(receipt,
            afterInsert: stage.StartsWith("receipt", StringComparison.Ordinal) ? () =>
            {
                if (stage == "receipt-failure") throw new InjectedFailure();
                canceled.Cancel();
            } : null));
        var operation = failed.ExecuteAsync(async token =>
        {
            var result = await failed.WriteFactsAsync(token);
            if (stage == "action-failure") throw new InjectedFailure();
            if (stage == "action-cancel") canceled.Cancel();
            return result;
        }, canceled.Token);
        if (stage.EndsWith("failure", StringComparison.Ordinal))
            await Assert.ThrowsExactlyAsync<InjectedFailure>(() => operation);
        else
            await Assert.ThrowsAsync<OperationCanceledException>(() => operation);
        Assert.IsNull(failed.Session.Transaction);
        using var retry = new Fixture(connection, keys, user);
        await retry.AssertFactsAsync(0, timeout.Token);
        var success = await retry.ExecuteAsync(retry.WriteFactsAsync, timeout.Token);
        Assert.IsFalse(success.IsReplay);
        Assert.AreEqual(1, success.Value.Number);
        await retry.AssertFactsAsync(1, timeout.Token);
        // failed 未释放：重试成功证明释放的是命令租约，而非靠关闭原连接解除会话锁。
        Assert.AreEqual(ConnectionState.Open, failed.Session.Connection.State);
    }

    /// <summary>第二个同身份请求在第一事务提交前等待租约且尚未开启事务，提交后取得新快照并重放。</summary>
    [TestMethod]
    [Timeout(60000)]
    public async Task Serializable_ConcurrentSameCommandWaitsBeforeSnapshotAndReplays()
    {
        var connection = PostgreSqlIntegrationEnvironment.GetCommandConnectionString();
        var keys = new TestKeys();
        var user = new TestUser(Guid.NewGuid());
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        var written = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var preparing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var first = new Fixture(connection, keys, user);
        using var second = new Fixture(connection, keys, user, receipt => new ReceiptProbe(receipt,
            beforePreparation: () => preparing.TrySetResult()));
        using var observer = new Fixture(connection, keys, user);
        var pid = await first.Session.Connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT pg_backend_pid()", cancellationToken: timeout.Token));
        var calls = 0;
        var firstTask = first.ExecuteAsync(async token =>
        {
            Interlocked.Increment(ref calls);
            var result = await first.WriteFactsAsync(token);
            written.TrySetResult();
            await release.Task.WaitAsync(token);
            return result;
        }, timeout.Token);
        Task<CommandExecutionResult<TestResult>>? secondTask = null;
        try
        {
            await written.Task.WaitAsync(timeout.Token);
            Assert.IsTrue(await observer.Session.Connection.ExecuteScalarAsync<long>(new CommandDefinition(
                "SELECT count(*) FROM pg_locks WHERE pid=@Pid AND locktype='advisory' AND granted",
                new { Pid = pid }, cancellationToken: timeout.Token)) > 0);
            secondTask = second.ExecuteAsync(async token =>
            {
                Interlocked.Increment(ref calls);
                return await second.WriteFactsAsync(token);
            }, timeout.Token);
            await preparing.Task.WaitAsync(timeout.Token);
            Assert.IsNull(second.Session.Transaction, "等待身份租约时不能提前建立 Serializable 快照。");
            Assert.IsFalse(secondTask.IsCompleted, "第一请求仍持有租约，第二请求应等待。");
            await observer.AssertFactsAsync(0, timeout.Token);
            release.TrySetResult();
            var results = await Task.WhenAll(firstTask, secondTask);
            Assert.IsFalse(results[0].IsReplay);
            Assert.IsTrue(results[1].IsReplay);
            Assert.AreEqual(results[0].Value, results[1].Value);
            Assert.AreEqual(1, calls);
            Assert.IsNull(first.Session.Transaction);
            Assert.IsNull(second.Session.Transaction);
            await observer.AssertFactsAsync(1, timeout.Token);
        }
        finally
        {
            // 断言失败也解除业务门闩并等待请求退出，避免已启动任务继续使用被释放的连接。
            release.TrySetResult();
            timeout.Cancel();
            try { await firstTask; } catch { /* 原始断言或请求异常由测试主体报告。 */ }
            if (secondTask is not null)
                try { await secondTask; } catch { /* 同上，仅收束并发任务。 */ }
        }
    }

    /// <summary>可序列化的原始业务结果，以身份和号码证明重放不重新生成结果。</summary>
    public sealed record TestResult(Guid SequenceUid, Guid LogUid, int Number);

    /// <summary>每例使用独立随机键，已提交数据保留且不与其他测试共享序列行。</summary>
    private sealed class TestKeys
    {
        public Guid CommandId { get; } = Guid.NewGuid();
        public Guid TargetUid { get; } = Guid.NewGuid();
        public string DocType { get; } = "__pgtest_" + Guid.NewGuid().ToString("N")[..12];
    }

    /// <summary>提供随机已认证身份，不创建或修改账户数据。</summary>
    private sealed class TestUser(Guid uid) : ICurrentUser
    {
        public Guid? UserId => uid;
        public string UserName => "__pgtest_executor";
        public bool IsAuthenticated => true;
    }

    /// <summary>只标识明确注入的回滚异常，避免把数据库或协议失败误判为预期失败。</summary>
    private sealed class InjectedFailure : Exception { }

    /// <summary>共享真实会话的执行器与三个仓储；不初始化、清理或删除任何数据库数据。</summary>
    private sealed class Fixture : IDisposable
    {
        /// <summary>本夹具固定操作标识，不与真实业务命令共享命名。</summary>
        public const string CommandType = "__pgtest_executor";
        private readonly TestKeys keys;
        private readonly TestUser user;
        private readonly PostgreSqlBusinessOperationLogRepository logs;
        private readonly PostgreSqlDocumentSequenceRepository numbers;
        public DbSession Session { get; }
        public BusinessCommandExecutor Executor { get; }

        /// <summary>构造真实仓储，仅允许在回执边界装饰故障和同步信号。</summary>
        public Fixture(string connection, TestKeys keys, TestUser user,
            Func<ICommandReceiptRepository, ICommandReceiptRepository>? decorate = null)
        {
            this.keys = keys;
            this.user = user;
            Session = new DbSession(new PostgreSqlConnectionFactory(connection));
            var context = new RepositoryBaseContext(Session, NullLoggerFactory.Instance, new AuditStampService(user), user);
            ICommandReceiptRepository receipts = new PostgreSqlCommandReceiptRepository(context);
            Executor = new BusinessCommandExecutor(new ServiceBaseContext(Session, NullLoggerFactory.Instance),
                decorate is null ? receipts : decorate(receipts), user);
            logs = new PostgreSqlBusinessOperationLogRepository(context);
            numbers = new PostgreSqlDocumentSequenceRepository(context);
        }

        /// <summary>由真实执行器拥有 Serializable 事务及提交，输入在重试间保持稳定。</summary>
        public Task<CommandExecutionResult<TestResult>> ExecuteAsync(Func<CancellationToken, Task<TestResult>> action, CancellationToken token) =>
            Executor.ExecuteAsync(keys.CommandId, CommandType, keys.TargetUid, new { Amount = 1m }, action, token, IsolationLevel.Serializable);

        /// <summary>在执行器事务中取号并追加真实审计，结果冻结两条业务事实的身份。</summary>
        public async Task<TestResult> WriteFactsAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Assert.IsNotNull(Session.Transaction);
            Assert.AreEqual(IsolationLevel.Serializable, Session.Transaction.IsolationLevel);
            var number = await numbers.GetNextAsync(keys.DocType, "202610", user.UserName);
            var log = new BusinessOperationLog
            {
                CommandId = keys.CommandId, Module = CommandType, AggregateType = "Test", AggregateUid = keys.TargetUid,
                OperationType = "Commit", OperationName = "命令事务集成验证", CorrelationId = keys.CommandId.ToString("N"),
                UserUid = user.UserId, UserNameSnapshot = user.UserName, OccurredDate = DateTime.Now
            };
            Assert.AreEqual(1, await logs.InsertAsync(log));
            return new TestResult(number.Uid, log.Uid, number.CurrentNo);
        }

        /// <summary>从当前独立连接核对每种事实各一行，序列值也必须与执行次数一致。</summary>
        public async Task AssertFactsAsync(int count, CancellationToken token)
        {
            const string sql = """
                SELECT (SELECT count(*) FROM public.command_receipt WHERE command_id=@CommandId) AS "Receipts",
                       (SELECT count(*) FROM public.business_operation_log WHERE command_id=@CommandId) AS "Logs",
                       (SELECT count(*) FROM public.document_sequence WHERE doc_type=@DocType) AS "Sequences",
                       COALESCE((SELECT current_no FROM public.document_sequence WHERE doc_type=@DocType AND year_month='202610'),0) AS "Number";
                """;
            var row = await Session.Connection.QuerySingleAsync(new CommandDefinition(sql,
                new { keys.CommandId, keys.DocType }, cancellationToken: token));
            Assert.AreEqual((long)count, (long)row.Receipts);
            Assert.AreEqual((long)count, (long)row.Logs);
            Assert.AreEqual((long)count, (long)row.Sequences);
            Assert.AreEqual(count, (int)row.Number);
        }

        /// <summary>保留完整行快照，检测重放或冲突是否意外改写身份、时间、结果或审计字段。</summary>
        public async Task<string> SnapshotAsync(CancellationToken token) => await Session.Connection.ExecuteScalarAsync<string>(new CommandDefinition("""
            SELECT jsonb_build_object(
                'receipts',(SELECT jsonb_agg(to_jsonb(r) ORDER BY uid) FROM public.command_receipt r WHERE command_id=@CommandId),
                'logs',(SELECT jsonb_agg(to_jsonb(l) ORDER BY uid) FROM public.business_operation_log l WHERE command_id=@CommandId),
                'sequences',(SELECT jsonb_agg(to_jsonb(s) ORDER BY uid) FROM public.document_sequence s WHERE doc_type=@DocType))::text;
            """, new { keys.CommandId, keys.DocType }, cancellationToken: token))
            ?? throw new AssertFailedException("完整事实快照不得为空。");

        /// <summary>释放连接；成功提交的数据由用户后续处理，本夹具不执行清理 SQL。</summary>
        public void Dispose() => Session.Dispose();
    }

    /// <summary>在真实回执边界提供可控信号与故障，完整转发事务前准备能力以保留真实锁协议。</summary>
    private sealed class ReceiptProbe(ICommandReceiptRepository inner, Action? beforePreparation = null,
        Action? afterInsert = null) : ICommandReceiptRepository, ICommandTransactionPreparation
    {
        /// <summary>先通知测试已进入准备阶段，再转发真实租约获取，不开启事务。</summary>
        public Task<IAsyncDisposable?> AcquireBeforeTransactionAsync(Guid commandId, CancellationToken token)
        {
            beforePreparation?.Invoke();
            return ((ICommandTransactionPreparation)inner).AcquireBeforeTransactionAsync(commandId, token);
        }

        /// <summary>转发真实事务内命令锁。</summary>
        public Task<int> LockAsync(Guid commandId, CancellationToken token) => inner.LockAsync(commandId, token);
        /// <summary>转发真实持久化回执查询。</summary>
        public Task<CommandReceipt?> FindAsync(Guid commandId, CancellationToken token) => inner.FindAsync(commandId, token);
        /// <summary>实际追加回执后注入失败或取消，验证已有写入仍由外层事务统一回滚。</summary>
        public async Task InsertAsync(CommandReceipt receipt, CancellationToken token)
        {
            await inner.InsertAsync(receipt, token);
            afterInsert?.Invoke();
        }
    }
}
