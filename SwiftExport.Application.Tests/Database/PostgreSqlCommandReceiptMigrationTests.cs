using Microsoft.VisualStudio.TestTools.UnitTesting;
using NpgsqlTypes;
using SwiftExport.Core.Entities.Commands;
using SwiftExport.Infrastructure.Repositories.Commands;

namespace SwiftExport.Application.Tests.Database;

/// <summary>复用真实 Dapper 与既有无网络夹具验证回执映射、参数、共享命令锁及事务边界。</summary>
public sealed partial class PostgreSqlRepositoryTests
{
    /// <summary>单次追加保留回执身份和版本，UTC 时间及本地审计时间使用不同 PostgreSQL 参数类型。</summary>
    [TestMethod]
    public async Task CommandReceiptInsert_PreservesIdentityAndTypedTimes()
    {
        using var fixture = new Fixture();
        var repository = new PostgreSqlCommandReceiptRepository(DictionaryContext(fixture));
        var receipt = OfflineReceipt();
        var uid = receipt.Uid;
        await repository.InsertAsync(receipt, default);
        Assert.AreEqual(uid, receipt.Uid);
        Assert.AreEqual(3, fixture.Connection.Values["Version"]);
        Assert.AreEqual(receipt.CommandId, fixture.Connection.Values["CommandId"]);
        Assert.AreEqual(DBNull.Value, fixture.Connection.Values["RequestTargetUid"]);
        Assert.AreEqual(NpgsqlDbType.TimestampTz, fixture.Connection.Types["RecordedAtUtc"]);
        Assert.AreEqual(NpgsqlDbType.Timestamp, fixture.Connection.Types["CreatedDate"]);
        Assert.AreEqual(DateTimeKind.Unspecified, ((DateTime)fixture.Connection.Values["CreatedDate"]!).Kind);
        StringAssert.Contains(fixture.Connection.Text, "INSERT INTO \"public\".\"command_receipt\"");
        Assert.IsFalse(fixture.Connection.Text.Contains("ON CONFLICT", StringComparison.Ordinal));
        Assert.AreSame(fixture.Session.Transaction, fixture.Connection.LastTransaction);
        Assert.AreEqual(0, fixture.Connection.Commits);
    }

    /// <summary>查询别名还原身份、可空目标、指纹和原始结果，未命中返回空而不写入。</summary>
    [TestMethod]
    public async Task CommandReceiptFind_MapsOriginalResultAndMissingReceipt()
    {
        using var fixture = new Fixture();
        var receipt = OfflineReceipt();
        var rows = fixture.Connection.Rows;
        rows.Columns.Add("Uid", typeof(Guid));
        rows.Columns.Add("CommandId", typeof(Guid));
        rows.Columns.Add("ActorUid", typeof(Guid));
        rows.Columns.Add("RequestTargetUid", typeof(Guid));
        rows.Columns.Add("RequestFingerprint", typeof(string));
        rows.Columns.Add("ResultJson", typeof(string));
        rows.Columns.Add("RecordedAtUtc", typeof(DateTime));
        rows.Rows.Add(receipt.Uid, receipt.CommandId, receipt.ActorUid, DBNull.Value,
            receipt.RequestFingerprint, receipt.ResultJson, receipt.RecordedAtUtc);
        var repository = new PostgreSqlCommandReceiptRepository(DictionaryContext(fixture));
        var result = await repository.FindAsync(receipt.CommandId, default);
        Assert.IsNotNull(result);
        Assert.AreEqual(receipt.Uid, result.Uid);
        Assert.AreEqual(receipt.CommandId, result.CommandId);
        Assert.AreEqual(receipt.ActorUid, result.ActorUid);
        Assert.IsNull(result.RequestTargetUid);
        Assert.AreEqual(receipt.ResultJson, result.ResultJson);
        Assert.AreEqual(receipt.RecordedAtUtc, result.RecordedAtUtc);
        StringAssert.Contains(fixture.Connection.Text, "\"recorded_at_utc\" AS \"RecordedAtUtc\"");
        StringAssert.Contains(fixture.Connection.Text, "WHERE command_id = @CommandId");
        Assert.AreSame(fixture.Session.Transaction, fixture.Connection.LastTransaction);
        rows.Rows.Clear();
        Assert.IsNull(await repository.FindAsync(receipt.CommandId, default));
        Assert.AreEqual(0, fixture.Connection.Commits);
    }

    /// <summary>命令锁使用主控共享能力及固定资源摘要，取消、事务缺失均不伪装为锁繁忙。</summary>
    [TestMethod]
    public async Task CommandReceiptLock_UsesStableKeyAndTransaction()
    {
        using var fixture = new Fixture();
        var repository = new PostgreSqlCommandReceiptRepository(DictionaryContext(fixture));
        Assert.AreEqual(0, await repository.LockAsync(Guid.Parse("00112233-4455-6677-8899-aabbccddeeff"), default));
        Assert.AreEqual(1973118291719236984L, fixture.Connection.Values["Key"]);
        StringAssert.Contains(fixture.Connection.Text, "pg_try_advisory_xact_lock");
        Assert.AreSame(fixture.Session.Transaction, fixture.Connection.LastTransaction);
        fixture.Session.Transaction!.Dispose();
        fixture.Session.Transaction = null;
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.LockAsync(Guid.NewGuid(), default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.InsertAsync(OfflineReceipt(), default));
        Assert.HasCount(1, fixture.Connection.Commands);
    }

    /// <summary>追加零行和数据库失败保留异常，仓储不能把失败写入当作成功或接管回滚。</summary>
    [TestMethod]
    public async Task CommandReceiptInsert_PropagatesWriteFailure()
    {
        using var fixture = new Fixture();
        var repository = new PostgreSqlCommandReceiptRepository(DictionaryContext(fixture));
        fixture.Connection.ExecuteWrite = () => 0;
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.InsertAsync(OfflineReceipt(), default));
        var failure = new InvalidOperationException("测试写入故障");
        fixture.Connection.ExecuteWrite = () => throw failure;
        var observed = await Assert.ThrowsAsync<InvalidOperationException>(() => repository.InsertAsync(OfflineReceipt(), default));
        Assert.AreSame(failure, observed);
        Assert.AreEqual(0, fixture.Connection.Rollbacks);
        Assert.IsNotNull(fixture.Session.Transaction);
    }

    /// <summary>三个方法均传递预取消令牌，不执行命令或清理调用方事务。</summary>
    [TestMethod]
    public async Task CommandReceiptQueries_ForwardCancellation()
    {
        using var fixture = new Fixture();
        var repository = new PostgreSqlCommandReceiptRepository(DictionaryContext(fixture));
        var token = new CancellationToken(true);
        await Assert.ThrowsAsync<OperationCanceledException>(() => repository.LockAsync(Guid.NewGuid(), token));
        await Assert.ThrowsAsync<OperationCanceledException>(() => repository.FindAsync(Guid.NewGuid(), token));
        await Assert.ThrowsAsync<OperationCanceledException>(() => repository.InsertAsync(OfflineReceipt(), token));
        Assert.HasCount(0, fixture.Connection.Commands);
        Assert.AreEqual(0, fixture.Connection.Rollbacks);
        Assert.IsNotNull(fixture.Session.Transaction);
    }

    /// <summary>构造合法回执数据，仅供无网络参数和映射测试。</summary>
    private static CommandReceipt OfflineReceipt() => new()
    {
        Uid = Guid.NewGuid(), Version = 3, CommandId = Guid.NewGuid(), ActorUid = Guid.NewGuid(),
        CommandType = "__pgtest_receipt__", RequestFingerprint = new string('A', 64),
        ResultJson = "{\"Value\":1}", RecordedAtUtc = DateTime.UtcNow
    };
}
