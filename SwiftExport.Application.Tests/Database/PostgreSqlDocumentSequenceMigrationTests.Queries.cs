using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Infrastructure.Repositories.Systems.Numbering;

namespace SwiftExport.Application.Tests.Database;

/// <summary>使用已有 Dapper 夹具验证取号 SQL、持久化投影与事务传递；竞争及回滚由实库测试证明。</summary>
public sealed partial class PostgreSqlRepositoryTests
{
    /// <summary>返回数据库实际 Uid 和版本，不把本次候选插入 Uid 冒充已有计数行的身份。</summary>
    [TestMethod]
    public async Task DocumentSequence_MapsPersistedIdentityAndPreservesSharedTransaction()
    {
        using var fixture = new Fixture();
        var uid = Guid.NewGuid();
        fixture.Connection.Rows.Columns.Add("Uid", typeof(Guid));
        fixture.Connection.Rows.Columns.Add("Version", typeof(int));
        fixture.Connection.Rows.Columns.Add("CurrentNo", typeof(int));
        fixture.Connection.Rows.Columns.Add("DocType", typeof(string));
        fixture.Connection.Rows.Columns.Add("YearMonth", typeof(string));
        fixture.Connection.Rows.Rows.Add(uid, 9, 42, "PO", "202610");
        var repository = new PostgreSqlDocumentSequenceRepository(DictionaryContext(fixture));
        var result = await repository.GetNextAsync("PO", "202610", "取号测试");
        Assert.AreEqual(uid, result.Uid);
        Assert.AreEqual(9, result.Version);
        Assert.AreEqual(42, result.CurrentNo);
        Assert.AreEqual("PO", result.DocType);
        Assert.AreEqual("202610", result.YearMonth);
        Assert.AreNotEqual(uid, (Guid)fixture.Connection.Values["Uid"]!);
        Assert.AreEqual("取号测试", fixture.Connection.Values["UpdatedBy"]);
        StringAssert.Contains(fixture.Connection.Text, "ON CONFLICT (doc_type, year_month) WHERE is_deleted = FALSE");
        StringAssert.Contains(fixture.Connection.Text, "current_no = target.current_no + 1");
        StringAssert.Contains(fixture.Connection.Text, "\"uid\" AS \"Uid\"");
        Assert.IsFalse(fixture.Connection.Text.Contains("version =", StringComparison.Ordinal));
        Assert.IsFalse(fixture.Connection.Text.Contains("nextval", StringComparison.Ordinal));
        Assert.AreSame(fixture.Session.Transaction, fixture.Connection.LastTransaction);
        Assert.HasCount(1, fixture.Connection.Commands);
        Assert.AreEqual(0, fixture.Connection.Commits);
        Assert.AreEqual(0, fixture.Connection.Rollbacks);
    }

    /// <summary>直接取号允许没有外层事务，NULL 操作者参数保留且 SQL 明确类型和 UTC 时间。</summary>
    [TestMethod]
    public async Task DocumentSequence_WithoutTransactionBindsNullOperator()
    {
        using var fixture = new Fixture();
        fixture.Session.Transaction!.Dispose();
        fixture.Session.Transaction = null;
        fixture.Connection.Rows.Columns.Add("CurrentNo", typeof(int));
        fixture.Connection.Rows.Rows.Add(1);
        var repository = new PostgreSqlDocumentSequenceRepository(DictionaryContext(fixture));
        Assert.AreEqual(1, (await repository.GetNextAsync("SO", "202610", null)).CurrentNo);
        Assert.AreEqual(DBNull.Value, fixture.Connection.Values["UpdatedBy"]);
        Assert.AreEqual("SO", fixture.Connection.Values["DocType"]);
        Assert.AreEqual("202610", fixture.Connection.Values["YearMonth"]);
        Assert.IsNull(fixture.Connection.LastTransaction);
        StringAssert.Contains(fixture.Connection.Text, "CAST(@UpdatedBy AS text)");
        StringAssert.Contains(fixture.Connection.Text, "clock_timestamp() AT TIME ZONE 'UTC'");
        Assert.HasCount(1, fixture.Connection.Commands);
    }
}
