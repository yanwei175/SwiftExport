using System.Buffers.Binary;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using SwiftExport.Infrastructure.Data;

namespace SwiftExport.Application.Tests.Database;

/// <summary>真实验证来源多资源获取的固定30秒预算与部分获取清理；只使用随机锁键，不写业务表或改生产时间配置。</summary>
[TestClass, DoNotParallelize, TestCategory("PostgreSqlIntegration")]
public sealed class PostgreSqlSourcePreparationDeadlineIntegrationTests
{
    private static readonly MethodInfo AcquireMethod = typeof(DbSession).Assembly
        .GetType("SwiftExport.Infrastructure.Data.PostgreSqlSourceDemandLock", true)!
        .GetMethod("AcquireBeforeTransactionAsync", BindingFlags.Static | BindingFlags.NonPublic)!;

    /// <summary>按实际签名键序先取得S再等待第二S，总预算耗尽返回null；独立目录证明无父快照、已取得锁清零及占位可复用或安全隔离。</summary>
    [TestMethod, Timeout(55000)]
    public async Task RealThirtySecondBudget_ReleasesPartialSourceLocks()
    {
        using var holder = Session(); using var waiter = Session(); using var observer = Session();
        var sources = new[] { Guid.NewGuid(), Guid.NewGuid() }.OrderBy(SourceKey).ToArray();
        var waiterPid = ((NpgsqlConnection)waiter.Connection).ProcessID;
        var held = await Acquire(holder, [sources[1]], [], CancellationToken.None);
        Assert.IsNotNull(held);
        using var safety = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        Task<IAsyncDisposable?>? pending = null;
        var elapsed = Stopwatch.StartNew();
        try
        {
            pending = Acquire(waiter, sources, [Guid.NewGuid()], safety.Token);
            using var observation = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            while (!await observer.Connection.ExecuteScalarAsync<bool>(new CommandDefinition("""
                SELECT EXISTS(SELECT 1 FROM pg_stat_activity a WHERE a.pid=@Pid AND a.backend_xid IS NULL
                  AND a.query LIKE '%pg_try_advisory_lock%')
                  AND (SELECT COUNT(*) FROM pg_locks WHERE pid=@Pid AND locktype='advisory' AND granted)=1
                """, new { Pid = waiterPid }, cancellationToken: observation.Token)))
                await Task.Delay(25, observation.Token);
            Assert.IsFalse(pending.IsCompleted); Assert.IsNull(waiter.Transaction);
            Assert.IsNull(await pending); elapsed.Stop();
            Assert.IsTrue(elapsed.Elapsed >= TimeSpan.FromSeconds(28), "必须实际等待生产30秒预算，不能提前模拟超时。");
            Assert.IsTrue(elapsed.Elapsed < TimeSpan.FromSeconds(40), "预算应覆盖整个资源获取，不能无限等待。");
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            while (await Locks(observer, waiterPid, cleanup.Token) != 0) await Task.Delay(25, cleanup.Token);
            Assert.IsNull(waiter.Transaction); Assert.AreEqual(1L, await Locks(observer, ((NpgsqlConnection)holder.Connection).ProcessID, cleanup.Token));
            if (((NpgsqlConnection)waiter.Connection).State == System.Data.ConnectionState.Open)
            {
                Assert.AreEqual(1, await waiter.Connection.ExecuteScalarAsync<int>("SELECT 1"));
                await using var again = await Acquire(waiter, [], [], cleanup.Token);
                Assert.IsNotNull(again); Assert.AreEqual(0L, await Locks(observer, waiterPid, cleanup.Token));
            }
            else
                await Assert.ThrowsAsync<ObjectDisposedException>(() => Acquire(waiter, [], [], cleanup.Token));
        }
        finally
        {
            safety.Cancel(); await held.DisposeAsync();
            if (pending is not null)
            {
                try { if (await pending is { } late) await late.DisposeAsync(); }
                catch (OperationCanceledException) { /* 用例提前失败也必须清理实际获取，保留原验证异常。 */ }
            }
        }
    }

    /// <summary>按生产冻结摘要计算排序，仅用于构造真实先后竞争，不替代生产获取。</summary>
    private static long SourceKey(Guid uid) => BinaryPrimitives.ReadInt64BigEndian(
        SHA256.HashData(Encoding.UTF8.GetBytes($"SwiftExport:SourceDemand:Source:{uid:N}")));

    /// <summary>严格已有故障库门禁，不引入连接默认值或新的数据库。</summary>
    private static DbSession Session() => new(new PostgreSqlConnectionFactory(PostgreSqlIntegrationEnvironment.GetFailureConnectionString()));

    /// <summary>反射调用内部真实能力，不修改生产资源/预算，也不新增运行公开接口。</summary>
    private static Task<IAsyncDisposable?> Acquire(DbSession session, Guid[] sources, Guid[] gross, CancellationToken token)
        => (Task<IAsyncDisposable?>)AcquireMethod.Invoke(null, [session, sources, gross, NullLogger.Instance, token])!;

    /// <summary>独立连接按旧物理PID观察，包括生产隔离后仍能核验锁已释放。</summary>
    private static Task<long> Locks(DbSession observer, int pid, CancellationToken token)
        => observer.Connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT COUNT(*) FROM pg_locks WHERE pid=@Pid AND locktype='advisory'", new { Pid = pid }, cancellationToken: token));
}
