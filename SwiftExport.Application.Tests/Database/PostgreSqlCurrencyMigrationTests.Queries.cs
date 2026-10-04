using Microsoft.VisualStudio.TestTools.UnitTesting;
using NpgsqlTypes;
using SwiftExport.Contracts.DTOs.SystemDtos.Requests;
using SwiftExport.Infrastructure.Repositories.Systems.ExchangeRates;

namespace SwiftExport.Application.Tests.Database;

/// <summary>复用无网络夹具验证汇率专属 SQL、Dapper 投影与事务；不替代数据库比较或并发验收。</summary>
public sealed partial class PostgreSqlRepositoryTests
{
    /// <summary>分页计数和取页共用参数，中文与可空汇率还原为 DTO，通配符参数保持原样。</summary>
    [TestMethod]
    public async Task ExchangePage_MapsDtoAndPreservesPagingAndPattern()
    {
        using var fixture = new Fixture();
        fixture.Connection.Rows.Columns.Add("FromCurrencyCode", typeof(string));
        fixture.Connection.Rows.Columns.Add("ToCurrencyCode", typeof(string));
        fixture.Connection.Rows.Columns.Add("Rate", typeof(decimal));
        fixture.Connection.Rows.Columns.Add("IsEnabled", typeof(bool));
        fixture.Connection.Rows.Columns.Add("Remark", typeof(string));
        fixture.Connection.Rows.Rows.Add("USD", "CNY", 7.123456m, false, "美元汇率");
        fixture.Connection.Rows.Rows.Add("EUR", "CNY", DBNull.Value, true, DBNull.Value);
        var repository = new PostgreSqlSysDicCurrencyExchangeRepository(DictionaryContext(fixture));
        var page = await repository.GetPagedAsync(new SysDicCurrencyExchangeSearchRequest
        { PageIndex = 0, PageSize = 500, Keyword = "  USD\\%_[A]  " });
        Assert.AreEqual(1, page.PageIndex);
        Assert.AreEqual(200, page.PageSize);
        Assert.AreEqual(1, page.TotalCount);
        Assert.AreEqual(7.123456m, page.Items[0].Rate);
        Assert.AreEqual("美元汇率", page.Items[0].Remark);
        Assert.IsFalse(page.Items[0].IsEnabled);
        Assert.AreEqual("CNY", page.Items[0].ToCurrencyCode);
        Assert.IsNull(page.Items[1].Rate);
        Assert.HasCount(2, fixture.Connection.Commands);
        foreach (var command in fixture.Connection.Commands)
        {
            Assert.AreSame(fixture.Session.Transaction, command.Transaction);
            Assert.AreEqual("%USD\\%_[A]%", command.Values["Keyword"]);
            Assert.AreEqual(0, command.Values["Offset"]);
            Assert.AreEqual(200, command.Values["PageSize"]);
            StringAssert.Contains(command.Sql, "from_currency_code LIKE @Keyword ESCAPE ''");
            StringAssert.Contains(command.Sql, "to_currency_code LIKE @Keyword ESCAPE ''");
            StringAssert.Contains(command.Sql, "remark LIKE @Keyword ESCAPE ''");
            Assert.IsFalse(command.Sql.Contains("USD", StringComparison.Ordinal));
        }
        StringAssert.Contains(fixture.Connection.Text, "\"rate\" AS \"Rate\"");
        StringAssert.Contains(fixture.Connection.Text, "from_currency_code NULLS FIRST, to_currency_code NULLS FIRST, uid");
        StringAssert.Contains(fixture.Connection.Text, "LIMIT @PageSize OFFSET @Offset");
    }

    /// <summary>空搜索条件不生成模式参数，非法页大小使用默认值，正常页码生成正确偏移。</summary>
    [TestMethod]
    public async Task ExchangePage_DefaultsAndBlankKeywordDoNotAddFilter()
    {
        using var fixture = new Fixture();
        var repository = new PostgreSqlSysDicCurrencyExchangeRepository(DictionaryContext(fixture));
        var page = await repository.GetPagedAsync(new SysDicCurrencyExchangeSearchRequest
        { PageIndex = 3, PageSize = -1, Keyword = "  " });
        Assert.AreEqual(3, page.PageIndex);
        Assert.AreEqual(20, page.PageSize);
        Assert.AreEqual(40, fixture.Connection.Values["Offset"]);
        Assert.IsFalse(fixture.Connection.Values.ContainsKey("Keyword"));
        Assert.IsFalse(fixture.Connection.Text.Contains("LIKE", StringComparison.Ordinal));
        page = await repository.GetPagedAsync(null!);
        Assert.AreEqual(1, page.PageIndex);
        Assert.AreEqual(20, page.PageSize);
    }

    /// <summary>全量锁读取保留停用汇率及可空字段，使用共享事务并按 Uid 排序锁定实体行。</summary>
    [TestMethod]
    public async Task ExchangeAllForUpdate_MapsEntityAndLocksOrderedRows()
    {
        using var fixture = new Fixture();
        var uid = Guid.NewGuid();
        fixture.Connection.Rows.Columns.Add("Uid", typeof(Guid));
        fixture.Connection.Rows.Columns.Add("Rate", typeof(decimal));
        fixture.Connection.Rows.Columns.Add("IsEnabled", typeof(bool));
        fixture.Connection.Rows.Rows.Add(uid, DBNull.Value, false);
        var repository = new PostgreSqlSysDicCurrencyExchangeRepository(DictionaryContext(fixture));
        var row = (await repository.GetAllForUpdateAsync()).Single();
        Assert.AreEqual(uid, row.Uid);
        Assert.IsNull(row.Rate);
        Assert.IsFalse(row.IsEnabled);
        StringAssert.Contains(fixture.Connection.Text, "ORDER BY uid FOR UPDATE");
        StringAssert.Contains(fixture.Connection.Text, "is_deleted = FALSE OR is_deleted IS NULL");
        StringAssert.Contains(fixture.Connection.Text, "\"rate\" AS \"Rate\"");
        Assert.AreSame(fixture.Session.Transaction, fixture.Connection.LastTransaction);
        Assert.IsFalse(fixture.Connection.Text.Contains("is_enabled =", StringComparison.Ordinal));
    }

    /// <summary>文本数组规范化后作为单参数绑定，锁查询投影仅含匹配端点，读取后去重且保留停用引用。</summary>
    [TestMethod]
    public async Task ExchangeReferences_BindsArrayAndProjectsBeforeDeduplication()
    {
        using var fixture = new Fixture();
        fixture.Connection.Rows.Columns.Add("FromCurrencyCode", typeof(string));
        fixture.Connection.Rows.Columns.Add("ToCurrencyCode", typeof(string));
        fixture.Connection.Rows.Rows.Add("USD", DBNull.Value);
        fixture.Connection.Rows.Rows.Add("USD", "CNY");
        fixture.Connection.Rows.Rows.Add(DBNull.Value, "CNY");
        var repository = new PostgreSqlSysDicCurrencyExchangeRepository(DictionaryContext(fixture));
        var codes = await repository.GetReferencedCurrencyCodesAsync([" USD ", "usd", " CNY ", " "]);
        CollectionAssert.AreEqual(new[] { "USD", "CNY" }, codes.ToArray());
        CollectionAssert.AreEqual(new[] { "USD", "CNY" }, (string[])fixture.Connection.Values["Codes"]!);
        Assert.AreEqual(NpgsqlDbType.Array | NpgsqlDbType.Text, fixture.Connection.Types["Codes"]);
        StringAssert.Contains(fixture.Connection.Text, "CASE WHEN rtrim(from_currency_code, ' ') = ANY(@Codes) THEN from_currency_code END");
        StringAssert.Contains(fixture.Connection.Text, "CASE WHEN rtrim(to_currency_code, ' ') = ANY(@Codes) THEN to_currency_code END");
        StringAssert.Contains(fixture.Connection.Text, "ORDER BY uid FOR UPDATE");
        Assert.IsFalse(fixture.Connection.Text.Contains("DISTINCT", StringComparison.Ordinal));
        Assert.IsFalse(fixture.Connection.Text.Contains("UNION", StringComparison.Ordinal));
        Assert.IsFalse(fixture.Connection.Text.Contains("is_enabled =", StringComparison.Ordinal));
        Assert.AreSame(fixture.Session.Transaction, fixture.Connection.LastTransaction);
    }

    /// <summary>两个锁入口无事务时在执行前拒绝；空编码集合即使无事务也不访问数据库。</summary>
    [TestMethod]
    public async Task ExchangeLocks_RequireTransactionAndEmptyCodesDoNotExecute()
    {
        using var fixture = new Fixture();
        var repository = new PostgreSqlSysDicCurrencyExchangeRepository(DictionaryContext(fixture));
        fixture.Session.Transaction!.Dispose();
        fixture.Session.Transaction = null;
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => repository.GetAllForUpdateAsync());
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => repository.GetReferencedCurrencyCodesAsync(["USD"]));
        Assert.HasCount(0, await repository.GetReferencedCurrencyCodesAsync([]));
        Assert.HasCount(0, await repository.GetReferencedCurrencyCodesAsync([" "]));
        Assert.HasCount(0, fixture.Connection.Commands);
    }

}
