using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Infrastructure.Repositories.Products;

namespace SwiftExport.Application.Tests.Database;

/// <summary>相关 BOM 读取复用记录连接，验证 PostgreSQL 输入范围、完整映射和调用方事务，不连接数据库。</summary>
public sealed partial class PostgreSqlRepositoryTests
{
    /// <summary>空身份集合不查询；当前组成由数据库联接确认，不额外锁定全图。</summary>
    [TestMethod]
    public async Task BomReferences_EmptyInputDoesNotQuery()
    {
        using var fixture = new Fixture();
        var context = DictionaryContext(fixture);
        Func<IEnumerable<Guid>, Task<IReadOnlyList<Bom>>> read =
            new PostgreSqlBomRepository(context).GetCurrentReferencesForProductsAsync;
        Assert.HasCount(0, await read([]));
        Assert.HasCount(0, await read([Guid.Empty]));
        Assert.HasCount(0, await read(null!));
        Assert.HasCount(0, fixture.Connection.Commands);
    }

    /// <summary>父或组件命中都参与查询，去掉空身份及重复参数；完整返回组成快照而不接管事务。</summary>
    [TestMethod]
    public async Task BomReferences_MapsColumnsAndSharesTransaction()
    {
        using var fixture = new Fixture();
        var parent = Guid.NewGuid();
        var component = Guid.NewGuid();
        var uid = Guid.NewGuid();
        fixture.Connection.Rows.Columns.Add("Uid", typeof(Guid));
        fixture.Connection.Rows.Columns.Add("ParentProductId", typeof(Guid));
        fixture.Connection.Rows.Columns.Add("ComponentProductId", typeof(Guid));
        fixture.Connection.Rows.Columns.Add("BomVersion", typeof(int));
        fixture.Connection.Rows.Columns.Add("ComponentSkuSnapshot", typeof(string));
        fixture.Connection.Rows.Rows.Add(uid, parent, DBNull.Value, 7, "旧快照");
        var context = DictionaryContext(fixture);
        var rows = await new PostgreSqlBomRepository(context).GetCurrentReferencesForProductsAsync([parent, Guid.Empty, component, parent]);
        var row = rows.Single();
        Assert.AreEqual(uid, row.Uid);
        Assert.AreEqual(parent, row.ParentProductId);
        Assert.IsNull(row.ComponentProductId);
        Assert.AreEqual(7, row.BomVersion);
        Assert.AreEqual("旧快照", row.ComponentSkuSnapshot);
        Assert.HasCount(1, fixture.Connection.Commands);
        Assert.AreSame(fixture.Session.Transaction, fixture.Connection.LastTransaction);
        var ids = fixture.Connection.Values.Values.SelectMany(value => value switch
        {
            Guid id => new[] { id },
            Guid[] array => array,
            _ => Array.Empty<Guid>()
        }).ToArray();
        CollectionAssert.AreEquivalent(new[] { parent, component }, ids);
        var sql = fixture.Connection.Text.ToLowerInvariant();
        StringAssert.Contains(sql, "parent_product_id");
        StringAssert.Contains(sql, "component_product_id");
        StringAssert.Contains(sql, " or ");
        StringAssert.Contains(sql, "exists");
        var normalized = BomQuerySql(sql).Replace("[", "").Replace("]", "");
        StringAssert.Contains(normalized, "parent.bom_version>0");
        StringAssert.Contains(normalized, "b.bom_version>0");
        Assert.IsFalse(normalized.Contains("b.parent_product_id=parent.uid", StringComparison.Ordinal));
        Assert.IsFalse(normalized.Contains("b.bom_version=parent.bom_version", StringComparison.Ordinal));
        Assert.IsFalse(sql.Contains("for update", StringComparison.Ordinal));
        Assert.IsFalse(sql.Contains("updlock", StringComparison.Ordinal));
        Assert.IsFalse(sql.Contains("holdlock", StringComparison.Ordinal));
        Assert.IsTrue(fixture.Connection.Types.Values.Contains(NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Uuid));
    }
}
