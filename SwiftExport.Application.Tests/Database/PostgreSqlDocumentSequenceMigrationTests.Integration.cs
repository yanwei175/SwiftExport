using System.Data;
using System.Diagnostics;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Core.Entities.Systems.Numbering;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.Repositories.Systems.Numbering;
using SwiftExport.Infrastructure.Services.Auth;

namespace SwiftExport.Application.Tests.Database;

/// <summary>在授权 PostgreSQL 库验证随机业务键取号，跨连接提交只用于竞争，最终精确清理本场景记录。</summary>
[TestClass]
[TestCategory("PostgreSqlIntegration")]
public sealed class PostgreSqlDocumentSequenceMigrationIntegrationTests
{
    /// <summary>无外层事务时自动提交；NULL 操作者、真实身份、版本及 UTC 审计保持取号约定。</summary>
    [TestMethod]
    public async Task Sequence_AutocommitPreservesIdentityVersionAndUtcAudit()
    {
        using var session = OpenSession();
        var docType = NewDocType();
        var otherDocType = NewDocType();
        await RequireEmptyAsync(session, docType);
        await RequireEmptyAsync(session, otherDocType);
        try
        {
            var repository = Repository(session, docType);
            var before = await UtcNowAsync(session);
            var first = await repository.GetNextAsync(docType, "202610", null);
            Assert.AreEqual(1, first.CurrentNo);
            Assert.AreEqual(1, first.Version);
            Assert.IsNull(first.CreatedBy);
            Assert.IsNull(first.UpdatedBy);
            Assert.IsNotNull(first.CreatedDate);
            Assert.IsNotNull(first.UpdatedDate);
            // 仅调整自己随机键的版本，证明递增不会把既有非默认版本覆写为 1。
            Assert.AreEqual(1, await session.Connection.ExecuteAsync(
                "UPDATE public.document_sequence SET version = 9 WHERE uid = @Uid AND doc_type = @DocType",
                new { first.Uid, DocType = docType }));
            var second = await repository.GetNextAsync(docType, "202610", docType);
            var after = await UtcNowAsync(session);
            Assert.AreEqual(first.Uid, second.Uid);
            Assert.AreEqual(2, second.CurrentNo);
            Assert.AreEqual(9, second.Version);
            Assert.AreEqual(first.CreatedDate, second.CreatedDate);
            Assert.IsNull(second.CreatedBy);
            Assert.AreEqual(docType, second.UpdatedBy);
            Assert.IsNotNull(second.UpdatedDate);
            Assert.IsTrue(first.CreatedDate.Value >= before && second.UpdatedDate.Value <= after);
            Assert.IsTrue(second.UpdatedDate.Value >= first.UpdatedDate.Value);
            using var observer = OpenSession();
            var persisted = await Repository(observer, docType).GetByIdAsync(second.Uid);
            Assert.IsNotNull(persisted);
            Assert.AreEqual(2, persisted.CurrentNo);
            Assert.AreEqual(9, persisted.Version);
            Assert.IsNull(session.Transaction);
            var independent = await repository.GetNextAsync(otherDocType, "202610", otherDocType);
            Assert.AreEqual(1, independent.CurrentNo);
            Assert.AreNotEqual(second.Uid, independent.Uid);
        }
        finally
        {
            try { await CleanupAsync(session, docType); }
            finally { await CleanupAsync(session, otherDocType); }
        }
    }

    /// <summary>新键和已有键在不同后端会话真实等待同一行或初始化锁，提交后得到连续不重复号码。</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Sequence_TwoConnectionsWaitAndAllocateDistinctNumbers(bool existing)
    {
        using var firstSession = OpenSession();
        using var secondSession = OpenSession();
        var docType = NewDocType();
        await RequireEmptyAsync(firstSession, docType);
        Task<DocumentSequence>? waiting = null;
        try
        {
            var firstRepository = Repository(firstSession, docType);
            var secondRepository = Repository(secondSession, docType);
            DocumentSequence? seed = existing ? await firstRepository.GetNextAsync(docType, "202610", docType) : null;
            var firstPid = await firstSession.Connection.ExecuteScalarAsync<int>("SELECT pg_backend_pid()");
            var secondPid = await secondSession.Connection.ExecuteScalarAsync<int>("SELECT pg_backend_pid()");
            Assert.AreNotEqual(firstPid, secondPid);
            firstSession.Transaction = firstSession.Connection.BeginTransaction(IsolationLevel.ReadCommitted);
            secondSession.Transaction = secondSession.Connection.BeginTransaction(IsolationLevel.ReadCommitted);
            // 限制失败场景的等待时间；只设置本事务，不改变连接池或数据库默认配置。
            await secondSession.Connection.ExecuteAsync("SET LOCAL statement_timeout = '10s'", transaction: secondSession.Transaction);
            var first = await firstRepository.GetNextAsync(docType, "202610", docType);
            waiting = secondRepository.GetNextAsync(docType, "202610", docType);
            var timer = Stopwatch.StartNew();
            var observedBlocking = false;
            while (timer.Elapsed < TimeSpan.FromSeconds(5) && !waiting.IsCompleted)
            {
                observedBlocking = await firstSession.Connection.ExecuteScalarAsync<bool>(
                    "SELECT @FirstPid = ANY(pg_blocking_pids(@SecondPid))",
                    new { FirstPid = firstPid, SecondPid = secondPid }, firstSession.Transaction);
                if (observedBlocking) break;
                await Task.Delay(25);
            }
            Assert.IsTrue(observedBlocking, "必须观察到本场景第二会话被第一会话阻塞，不能用时间延迟替代锁证据。");
            Assert.IsFalse(waiting.IsCompleted);
            firstSession.Transaction.Commit();
            EndTransaction(firstSession);
            var second = await waiting;
            secondSession.Transaction.Commit();
            EndTransaction(secondSession);
            Assert.AreEqual(existing ? 2 : 1, first.CurrentNo);
            Assert.AreEqual(existing ? 3 : 2, second.CurrentNo);
            Assert.AreEqual(first.Uid, second.Uid);
            if (seed is not null) Assert.AreEqual(seed.Uid, first.Uid);
            var persisted = await firstRepository.GetByIdAsync(first.Uid);
            Assert.IsNotNull(persisted);
            Assert.AreEqual(second.CurrentNo, persisted.CurrentNo);
            Assert.AreEqual(1, persisted.Version);
            Assert.AreEqual(1, await firstSession.Connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM public.document_sequence WHERE doc_type = @DocType AND year_month = '202610' AND is_deleted = FALSE",
                new { DocType = docType }));
        }
        finally
        {
            // 先释放第一会话的锁，再收束第二会话任务及其事务，最后才清理提交的测试记录。
            try { RollbackTransaction(firstSession); }
            finally
            {
                try
                {
                    if (waiting is not null)
                    {
                        try { await waiting; }
                        catch { /* 主路径已报告失败；观察任务异常，不能覆盖原断言或数据库异常。 */ }
                    }
                }
                finally
                {
                    try { RollbackTransaction(secondSession); }
                    finally { await CleanupAsync(firstSession, docType); }
                }
            }
        }
    }

    /// <summary>外层失败回滚新键初始化及已有键递增，连同版本和审计字段完整恢复。</summary>
    [TestMethod]
    public async Task Sequence_RollbackRestoresNewAndExistingKeys()
    {
        using var session = OpenSession();
        var docType = NewDocType();
        await RequireEmptyAsync(session, docType);
        try
        {
            var repository = Repository(session, docType);
            var seed = await repository.GetNextAsync(docType, "202610", docType);
            session.Transaction = session.Connection.BeginTransaction();
            var fresh = await repository.GetNextAsync(docType, "202611", null);
            var changed = await repository.GetNextAsync(docType, "202610", null);
            Assert.AreEqual(1, fresh.CurrentNo);
            Assert.AreEqual(2, changed.CurrentNo);
            Assert.AreEqual(seed.Uid, changed.Uid);
            // 模拟取号之后的外层业务失败，不让仓储提交共享事务。
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => FailAfterAllocationAsync());
            RollbackTransaction(session);
            Assert.IsNull(await repository.GetByIdAsync(fresh.Uid));
            var restored = await repository.GetByIdAsync(seed.Uid);
            Assert.IsNotNull(restored);
            Assert.AreEqual(seed.CurrentNo, restored.CurrentNo);
            Assert.AreEqual(seed.Version, restored.Version);
            Assert.AreEqual(seed.CreatedDate, restored.CreatedDate);
            Assert.AreEqual(seed.UpdatedDate, restored.UpdatedDate);
            Assert.AreEqual(seed.CreatedBy, restored.CreatedBy);
            Assert.AreEqual(seed.UpdatedBy, restored.UpdatedBy);
            Assert.AreEqual(2, (await repository.GetNextAsync(docType, "202610", docType)).CurrentNo);
            Assert.AreEqual(1, (await repository.GetNextAsync(docType, "202611", docType)).CurrentNo);
        }
        finally
        {
            try { RollbackTransaction(session); }
            finally { await CleanupAsync(session, docType); }
        }
    }

    /// <summary>自己的软删除历史不占活动唯一键，重新初始化返回新身份且不改历史计数。</summary>
    [TestMethod]
    public async Task Sequence_SoftDeletedHistoryAllowsNewActiveIdentity()
    {
        using var session = OpenSession();
        var docType = NewDocType();
        await RequireEmptyAsync(session, docType);
        session.Transaction = session.Connection.BeginTransaction();
        try
        {
            var repository = Repository(session, docType);
            var old = await repository.GetNextAsync(docType, "202610", docType);
            Assert.IsTrue((await repository.DeleteAsync(old.Uid, old.Version)).Success);
            var fresh = await repository.GetNextAsync(docType, "202610", null);
            Assert.AreNotEqual(old.Uid, fresh.Uid);
            Assert.AreEqual(1, fresh.CurrentNo);
            Assert.AreEqual(1, fresh.Version);
            Assert.IsNull(fresh.CreatedBy);
            Assert.IsNull(fresh.UpdatedBy);
            Assert.AreEqual(1, await session.Connection.ExecuteScalarAsync<int>(
                "SELECT current_no FROM public.document_sequence WHERE uid = @Uid AND doc_type = @DocType AND is_deleted = TRUE",
                new { old.Uid, DocType = docType }, session.Transaction));
        }
        finally
        {
            try { RollbackTransaction(session); }
            finally { await CleanupAsync(session, docType); }
        }
    }

    /// <summary>只通过统一环境闸门打开用户授权实例，不提供默认连接或输出凭据。</summary>
    private static DbSession OpenSession() => new(new PostgreSqlConnectionFactory(PostgreSqlIntegrationEnvironment.GetConnectionString()));

    /// <summary>创建本场景的随机类型，不使用真实单据类型。</summary>
    private static string NewDocType() => "__pgtest_" + Guid.NewGuid().ToString("N");

    /// <summary>为真实仓储提供同一会话及可识别测试审计身份。</summary>
    private static PostgreSqlDocumentSequenceRepository Repository(DbSession session, string marker)
    {
        var user = new NumberingUser(marker);
        return new(new RepositoryBaseContext(session, NullLoggerFactory.Instance, new AuditStampService(user), user));
    }

    /// <summary>写入之前核验随机类型没有历史或活动记录，碰撞时失败而非覆盖数据。</summary>
    private static async Task RequireEmptyAsync(DbSession session, string docType) => Assert.AreEqual(0,
        await session.Connection.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM public.document_sequence WHERE doc_type = @DocType", new { DocType = docType }));

    /// <summary>从服务器读取 UTC 墙上时间，避免测试机器时钟差异。</summary>
    private static Task<DateTime> UtcNowAsync(DbSession session) => session.Connection.ExecuteScalarAsync<DateTime>("SELECT clock_timestamp() AT TIME ZONE 'UTC'");

    /// <summary>制造取号后的业务失败，不向数据库注入失败 SQL。</summary>
    private static Task FailAfterAllocationAsync() => Task.FromException(new InvalidOperationException("测试外层业务失败"));

    /// <summary>释放测试拥有的已结束事务引用，仓储没有事务提交或清理权。</summary>
    private static void EndTransaction(DbSession session)
    {
        session.Transaction?.Dispose();
        session.Transaction = null;
    }

    /// <summary>回滚测试尚未结束的事务，释放锁后才能执行残留清理。</summary>
    private static void RollbackTransaction(DbSession session)
    {
        try { session.Transaction?.Rollback(); }
        finally { EndTransaction(session); }
    }

    /// <summary>仅发现本随机类型的精确身份，逐 Uid、类型、年月删除并核验零残留，不执行范围删除。</summary>
    private static async Task CleanupAsync(DbSession session, string docType)
    {
        var owned = await session.Connection.QueryAsync<DocumentSequence>(
            "SELECT uid AS \"Uid\", doc_type AS \"DocType\", year_month AS \"YearMonth\" FROM public.document_sequence WHERE doc_type = @DocType",
            new { DocType = docType });
        foreach (var row in owned)
            await session.Connection.ExecuteAsync(
                "DELETE FROM public.document_sequence WHERE uid = @Uid AND doc_type = @DocType AND year_month = @YearMonth",
                new { row.Uid, DocType = docType, row.YearMonth });
        await RequireEmptyAsync(session, docType);
    }

    /// <summary>测试审计标记不对应真实账号，不写用户或授权数据。</summary>
    private sealed class NumberingUser(string marker) : ICurrentUser
    {
        /// <summary>使用本场景前缀标记审计身份。</summary>
        public string UserName => marker;
        /// <summary>不关联真实用户。</summary>
        public Guid? UserId => null;
        /// <summary>仅满足审计接口。</summary>
        public bool IsAuthenticated => true;
    }
}
