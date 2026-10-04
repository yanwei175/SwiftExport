using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Infrastructure.Repositories.Inventory.Warehouses;

namespace SwiftExport.Application.Tests.Database;

/// <summary>通过共享无网络夹具核对候选投影、资格 SQL、字面参数及事务边界；引擎语义由实库测试验证。</summary>
public sealed partial class PostgreSqlRepositoryTests
{
    /// <summary>候选 DTO 显式映射真实供应商字段，计数和分页共享资格、参数及调用方事务。</summary>
    [TestMethod]
    public async Task WarehouseCandidate_MapsCurrentIdentityAndStablePaging()
    {
        using var fixture = new Fixture();
        var uid = Guid.NewGuid();
        var supplier = Guid.NewGuid();
        fixture.Connection.Rows.Columns.Add("Uid", typeof(Guid));
        fixture.Connection.Rows.Columns.Add("Version", typeof(int));
        fixture.Connection.Rows.Columns.Add("Code", typeof(string));
        fixture.Connection.Rows.Columns.Add("SupplierId", typeof(Guid));
        fixture.Connection.Rows.Columns.Add("SupplierCode", typeof(string));
        fixture.Connection.Rows.Columns.Add("SupplierName", typeof(string));
        fixture.Connection.Rows.Columns.Add("SupplierIsActive", typeof(bool));
        fixture.Connection.Rows.Rows.Add(uid, 3, "W01", supplier, "S01", "真实供应商", true);
        var result = await new PostgreSqlWarehouseCandidateRepository(DictionaryContext(fixture)).SearchAsync(
            new PagedRequestBase { PageIndex = 3, PageSize = 100, Keyword = " ~%_[\\ " }, default);
        Assert.AreEqual(1, result.TotalCount);
        Assert.AreEqual(3, result.PageIndex);
        Assert.AreEqual(100, result.PageSize);
        Assert.AreEqual(uid, result.Items.Single().Uid);
        Assert.AreEqual(3, result.Items.Single().Version);
        Assert.AreEqual(supplier, result.Items.Single().SupplierId);
        Assert.AreEqual("真实供应商", result.Items.Single().SupplierName);
        Assert.IsTrue(result.Items.Single().SupplierIsActive);
        Assert.HasCount(2, fixture.Connection.Commands);
        foreach (var command in fixture.Connection.Commands)
        {
            Assert.AreSame(fixture.Session.Transaction, command.Transaction);
            Assert.AreEqual("% ~~~%~_~[\\ %", command.Values["Keyword"]);
            Assert.AreEqual(200L, command.Values["Skip"]);
            Assert.AreEqual(100, command.Values["Take"]);
            Assert.AreEqual((int)WarehouseSiteTypeEnum.SupplierVirtual, command.Values["SiteType"]);
            StringAssert.Contains(command.Sql, "JOIN public.supplier s ON s.uid=w.supplier_id");
            StringAssert.Contains(command.Sql, "w.is_active=TRUE");
            StringAssert.Contains(command.Sql, "s.is_active=TRUE");
            StringAssert.Contains(command.Sql, "s.uid<>@EmptyUid");
            StringAssert.Contains(command.Sql, "btrim(s.supplier_name COLLATE \"C\",@Whitespace)");
            StringAssert.Contains(command.Sql, "COLLATE public.erp_dictionary_ci LIKE CAST(@Keyword AS text) ESCAPE '~'");
            Assert.IsFalse(command.Sql.Contains("snapshot", StringComparison.OrdinalIgnoreCase));
            Assert.IsFalse(command.Sql.Contains("SELECT *", StringComparison.Ordinal));
        }
        StringAssert.Contains(fixture.Connection.Text, "ORDER BY w.code COLLATE public.erp_dictionary_ci,w.uid LIMIT @Take OFFSET @Skip");
    }

    /// <summary>无关键词使用可空参数；Unicode 空白集合与运行时完全一致，零宽字符不能误归为空白。</summary>
    [TestMethod]
    public async Task WarehouseCandidate_NullKeywordAndExactUnicodeWhitespace()
    {
        using var fixture = new Fixture();
        await new PostgreSqlWarehouseCandidateRepository(DictionaryContext(fixture)).SearchAsync(new PagedRequestBase(), default);
        Assert.AreEqual(DBNull.Value, fixture.Connection.Values["Keyword"]);
        var expected = new string(Enumerable.Range(0, char.MaxValue + 1).Select(value => (char)value).Where(char.IsWhiteSpace).ToArray());
        Assert.AreEqual(expected, fixture.Connection.Values["Whitespace"]);
        Assert.IsFalse(((string)fixture.Connection.Values["Whitespace"]!).Contains('\u200B'));
        Assert.AreEqual(Guid.Empty, fixture.Connection.Values["EmptyUid"]);
    }

    /// <summary>未提供事务或已取消时不发出查询，不接管调用方连接或偷偷开启事务。</summary>
    [TestMethod]
    public async Task WarehouseCandidate_RequiresTransactionAndHonorsCancellation()
    {
        using var fixture = new Fixture();
        var repository = new PostgreSqlWarehouseCandidateRepository(DictionaryContext(fixture));
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => repository.SearchAsync(new PagedRequestBase(), canceled.Token));
        fixture.Session.Transaction!.Dispose();
        fixture.Session.Transaction = null;
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => repository.SearchAsync(new PagedRequestBase(), default));
        Assert.HasCount(0, fixture.Connection.Commands);
    }
}
