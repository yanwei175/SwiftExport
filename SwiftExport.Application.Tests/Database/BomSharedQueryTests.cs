using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Infrastructure.Repositories.Products;

namespace SwiftExport.Application.Tests.Database;

/// <summary>两提供者共享组成普通读取的SQL契约，验证资格查询不受单个来源父身份约束。</summary>
public sealed partial class PostgreSqlRepositoryTests
{
    /// <summary>保存事实单语句复用事务，空实体及请求空组独立映射，不将物理行复制成产品边。</summary>
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public async Task BomSharedFacts_MapsIndependentFactsAndPreservesInputs(bool postgres, bool withUpdLock)
    {
        using var fixture = new Fixture();
        var table = new System.Data.DataTable();
        table.Columns.Add("GroupKey", typeof(long));
        table.Columns.Add("BomNo", typeof(string));
        table.Columns.Add("Kind", typeof(int));
        table.Columns.Add("InputIndex", typeof(int));
        table.Columns.Add("Uid", typeof(Guid));
        table.Columns.Add("ComponentProductId", typeof(Guid));
        table.Columns.Add("uid", typeof(Guid));
        table.Columns.Add("Sku", typeof(string));
        var line = Guid.NewGuid();
        var child = Guid.NewGuid();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var nil = DBNull.Value;
        table.Rows.Add(7L, "B", 0, nil, line, child, nil, nil);
        table.Rows.Add(7L, "b ", 1, nil, nil, nil, first, "First");
        table.Rows.Add(7L, "Ｂ", 1, nil, nil, nil, second, "Second");
        table.Rows.Add(7L, "B", 2, 0, nil, nil, nil, nil);
        table.Rows.Add(7L, "B", 2, 1, nil, nil, nil, nil);
        table.Rows.Add(8L, " B", 2, 2, nil, nil, nil, nil);
        fixture.Connection.Rows = table;
        var context = DictionaryContext(fixture);
        var facts = postgres
            ? await new PostgreSqlBomRepository(context).GetSharedSaveFactsAsync(["B", "B", " B"], withUpdLock)
            : await new BomRepository(context).GetSharedSaveFactsAsync(["B", "B", " B"], withUpdLock);
        var group = facts.Single(row => row.GroupKey == 7);
        Assert.AreEqual(line, group.Lines.Single().Uid);
        Assert.AreEqual(child, group.Lines.Single().ComponentProductId);
        CollectionAssert.AreEquivalent(new[] { first, second }, group.Products.Select(row => row.Uid).ToArray());
        CollectionAssert.AreEqual(new[] { 0, 1 }, group.InputIndexes.ToArray());
        Assert.HasCount(0, facts.Single(row => row.GroupKey == 8).Lines);
        Assert.HasCount(0, facts.Single(row => row.GroupKey == 8).Products);
        Assert.AreSame(fixture.Session.Transaction, fixture.Connection.LastTransaction);
        var sql = BomQuerySql(fixture.Connection.Text);
        StringAssert.Contains(sql, "dense_rank()");
        Assert.IsFalse(sql.Contains("forupdate", StringComparison.Ordinal));
        Assert.AreEqual(!postgres && withUpdLock, sql.Contains("updlock", StringComparison.Ordinal));
        Assert.AreEqual(!postgres && withUpdLock, sql.Contains("holdlock", StringComparison.Ordinal));
        if (postgres)
            CollectionAssert.AreEqual(new[] { "B", "B", " B" }, (string[])fixture.Connection.Values["RequestedBomNos"]!);
        else
            Assert.AreEqual(" B", fixture.Connection.Values["RequestedBomNo2"]);
    }

    /// <summary>DTO、实体及分页均使用存在判断，避免多引用父联接放大；继续复用当前事务且不增加锁。</summary>
    [TestMethod]
    [DataRow(false, "dto")]
    [DataRow(false, "entity")]
    [DataRow(false, "page")]
    [DataRow(true, "dto")]
    [DataRow(true, "entity")]
    [DataRow(true, "page")]
    public async Task BomSharedQuery_UsesExistenceAndCallerTransaction(bool postgres, string operation)
    {
        using var fixture = new Fixture();
        var context = DictionaryContext(fixture);
        var pg = new PostgreSqlBomRepository(context);
        var sqlServer = new BomRepository(context);
        if (operation == "page")
        {
            var count = new System.Data.DataTable();
            count.Columns.Add("Count", typeof(int));
            count.Rows.Add(0);
            var rows = new System.Data.DataTable();
            rows.Columns.Add("Uid", typeof(Guid));
            fixture.Connection.ResultSets = [count, rows];
            if (postgres) await pg.GetPagedAsync(new()); else await sqlServer.GetPagedAsync(new());
        }
        else if (operation == "dto")
        {
            if (postgres) await pg.GetBomDtoByBomNoAsync("Shared"); else await sqlServer.GetBomDtoByBomNoAsync("Shared");
        }
        else
        {
            if (postgres) await pg.GetBomByBomNoAsync("Shared"); else await sqlServer.GetBomByBomNoAsync("Shared");
        }
        var sql = BomQuerySql(fixture.Connection.Text).Replace("[", "").Replace("]", "");
        StringAssert.Contains(sql, "exists(select1from");
        Assert.IsFalse(sql.Contains(postgres ? "b.parent_product_id=parent.uid" : "b.parentproductid=parent.uid", StringComparison.Ordinal));
        Assert.IsFalse(sql.Contains("forupdate", StringComparison.Ordinal));
        Assert.IsFalse(sql.Contains("updlock", StringComparison.Ordinal));
        Assert.IsFalse(sql.Contains("holdlock", StringComparison.Ordinal));
        Assert.AreSame(fixture.Session.Transaction, fixture.Connection.LastTransaction);
    }
}
