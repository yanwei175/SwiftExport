using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Infrastructure.Repositories.Products;

namespace SwiftExport.Application.Tests.Database;

/// <summary>验证集合读取的单次枚举、精确分组和事务传递，不以记录连接代替实库排序规则验证。</summary>
public sealed partial class PostgreSqlRepositoryTests
{
    /// <summary>空输入不访问连接，空白文本保留给数据库匹配，不擅自规范化业务编号。</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task BomCollection_EmptyInputDoesNotQuery(bool postgres)
    {
        using var fixture = new Fixture();
        var context = DictionaryContext(fixture);
        Func<IEnumerable<string>, Task<Dictionary<string, IEnumerable<Bom>>>> read = postgres
            ? new PostgreSqlBomRepository(context).GetBomsCollectionByBomNoListAsync
            : new BomRepository(context).GetBomsCollectionByBomNoListAsync;
        Assert.HasCount(0, await read(null!));
        Assert.HasCount(0, await read([]));
        Assert.HasCount(0, fixture.Connection.Commands);
    }

    /// <summary>调用方事务必须传入Dapper；保留历史、软删及原始编号精确分组，输入迭代器只能消费一次。</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task BomCollection_PreservesStoredGroupsAndUsesCallerTransaction(bool postgres)
    {
        using var fixture = new Fixture();
        fixture.Connection.Rows.Columns.Add("Uid", typeof(Guid));
        fixture.Connection.Rows.Columns.Add("BomNo", typeof(string));
        fixture.Connection.Rows.Columns.Add("BomVersion", typeof(int));
        fixture.Connection.Rows.Columns.Add("IsDeleted", typeof(bool));
        fixture.Connection.Rows.Rows.Add(Guid.NewGuid(), "ABC", 1, true);
        fixture.Connection.Rows.Rows.Add(Guid.NewGuid(), "abc ", 7, false);
        var enumerations = 0;
        IEnumerable<string> Once()
        {
            Assert.AreEqual(1, ++enumerations, "集合输入被重复枚举。");
            yield return " ABC";
            yield return "abc ";
        }
        var context = DictionaryContext(fixture);
        var result = postgres
            ? await new PostgreSqlBomRepository(context).GetBomsCollectionByBomNoListAsync(Once())
            : await new BomRepository(context).GetBomsCollectionByBomNoListAsync(Once());
        Assert.AreEqual(1, enumerations);
        Assert.HasCount(2, result);
        Assert.IsTrue(result["ABC"].Single().IsDeleted);
        Assert.AreEqual(7, result["abc "].Single().BomVersion);
        Assert.AreSame(fixture.Session.Transaction, fixture.Connection.LastTransaction);
        Assert.HasCount(1, fixture.Connection.Commands);
        var values = fixture.Connection.Values.Values.SelectMany(value => value switch
        { string text => new[] { text }, string[] texts => texts, _ => Array.Empty<string>() }).ToArray();
        CollectionAssert.AreEquivalent(new[] { " ABC", "abc " }, values);
        var sql = fixture.Connection.Text.ToLowerInvariant();
        Assert.IsFalse(sql.Contains("select *", StringComparison.Ordinal));
        Assert.IsFalse(sql.Contains("join", StringComparison.Ordinal));
        Assert.IsFalse(sql.Contains("for update", StringComparison.Ordinal));
        Assert.IsFalse(sql.Contains("updlock", StringComparison.Ordinal));
        if (postgres)
        {
            StringAssert.Contains(sql, "exists");
            Assert.IsTrue(fixture.Connection.Types.Values.Contains(NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Text));
        }
    }
}
