using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Infrastructure.Repositories.Products.Categories;

namespace SwiftExport.Application.Tests.Database;

/// <summary>通过既有离线夹具检查分类迁移的投影、参数、事务与有限递归边界。</summary>
public sealed partial class PostgreSqlRepositoryTests
{
    /// <summary>递归显式投影通过多映射读取深度，根和一百层返回原实体，一百零一层固定失败而不截断。</summary>
    [TestMethod]
    [DataRow(100, false)]
    [DataRow(101, true)]
    public async Task CategoryTree_PreservesProjectionAndRecursionLimit(int depth, bool fails)
    {
        using var fixture = new Fixture();
        fixture.Session.Transaction = null;
        fixture.Connection.Rows.Columns.Add("Uid", typeof(Guid));
        fixture.Connection.Rows.Columns.Add("CategoryName", typeof(string));
        fixture.Connection.Rows.Columns.Add("ParentId", typeof(Guid));
        fixture.Connection.Rows.Columns.Add("Depth", typeof(int));
        var root = Guid.NewGuid();
        fixture.Connection.Rows.Rows.Add(root, "根", DBNull.Value, 0);
        fixture.Connection.Rows.Rows.Add(Guid.NewGuid(), "子", root, depth);
        var repository = new PostgreSqlProductCategoryRepository(DictionaryContext(fixture));
        if (fails)
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => repository.GetSubById(root));
        else
        {
            var rows = await repository.GetSubById(root);
            Assert.HasCount(2, rows);
            Assert.IsTrue(rows.All(row => row.GetType() == typeof(ProductCategory)));
            Assert.AreEqual(root, rows[0].Uid);
            Assert.AreEqual(root, rows[1].ParentId);
        }
        Assert.AreEqual(root, fixture.Connection.Values["ParentId"]);
        StringAssert.Contains(fixture.Connection.Text, "WITH RECURSIVE category_tree");
        StringAssert.Contains(fixture.Connection.Text, "ct.depth<101");
        StringAssert.Contains(fixture.Connection.Text, "c.depth AS \"Depth\"");
        Assert.IsFalse(fixture.Connection.Text.Contains("FOR UPDATE", StringComparison.Ordinal));
        Assert.IsFalse(fixture.Connection.Text.Contains("SELECT *", StringComparison.Ordinal));
        Assert.IsNull(fixture.Connection.LastTransaction);
    }

    /// <summary>维护锁沿用事务与身份顺序；引用去重发生在读取后，只查底层主型号。</summary>
    [TestMethod]
    public async Task CategoryLocks_RequireTransactionAndDeduplicateMasterReferences()
    {
        using var fixture = new Fixture();
        var repository = new PostgreSqlProductCategoryRepository(DictionaryContext(fixture));
        fixture.Connection.Rows.Columns.Add("Uid", typeof(Guid));
        var uid = Guid.NewGuid();
        fixture.Connection.Rows.Rows.Add(uid);
        Assert.AreEqual(uid, (await repository.GetAllForUpdateAsync()).Single().Uid);
        StringAssert.Contains(fixture.Connection.Text, "ORDER BY c.uid FOR UPDATE");
        StringAssert.Contains(fixture.Connection.Text, "\"category_code\" AS \"CategoryCode\"");
        fixture.Connection.Rows = new System.Data.DataTable();
        fixture.Connection.Rows.Columns.Add("category_id", typeof(Guid));
        fixture.Connection.Rows.Rows.Add(uid);
        fixture.Connection.Rows.Rows.Add(uid);
        CollectionAssert.AreEqual(new[] { uid }, (await repository.GetReferencedCategoryIdsAsync([Guid.Empty, uid, uid])).ToArray());
        CollectionAssert.AreEqual(new[] { uid }, (Guid[])fixture.Connection.Values["Uids"]!);
        StringAssert.Contains(fixture.Connection.Text, "public.product_master pm");
        StringAssert.Contains(fixture.Connection.Text, "ORDER BY pm.uid FOR UPDATE");
        Assert.IsFalse(fixture.Connection.Text.Contains("DISTINCT", StringComparison.Ordinal));
        Assert.IsTrue(fixture.Connection.Commands.All(command => ReferenceEquals(command.Transaction, fixture.Session.Transaction)));
        fixture.Session.Transaction!.Dispose();
        fixture.Session.Transaction = null;
        var commandCount = fixture.Connection.Commands.Count;
        Assert.HasCount(0, await repository.GetReferencedCategoryIdsAsync([Guid.Empty]));
        Assert.HasCount(0, await repository.GetReferencedCategoryIdsAsync(null!));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => repository.GetReferencedCategoryIdsAsync([uid]));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => repository.GetAllForUpdateAsync());
        Assert.AreEqual(commandCount, fixture.Connection.Commands.Count);
        Assert.AreEqual(0, fixture.Connection.Commits);
        Assert.AreEqual(0, fixture.Connection.Rollbacks);
    }

    /// <summary>分类分页不裁剪请求页大小、不修剪关键词，保持窄投影和空值优先且无锁。</summary>
    [TestMethod]
    [DataRow(0, 20)]
    [DataRow(999, 999)]
    public async Task CategoryPage_PreservesSizeAndPatternWithoutLocks(int size, int expectedSize)
    {
        using var fixture = new Fixture();
        fixture.Connection.Rows.Columns.Add("CategoryName", typeof(string));
        fixture.Connection.Rows.Columns.Add("FakeName", typeof(string));
        fixture.Connection.Rows.Columns.Add("CategoryCode", typeof(string));
        fixture.Connection.Rows.Columns.Add("ParentId", typeof(Guid));
        fixture.Connection.Rows.Rows.Add("分类", DBNull.Value, DBNull.Value, DBNull.Value);
        var repository = new PostgreSqlProductCategoryRepository(DictionaryContext(fixture));
        var page = await repository.GetPagedAsync(new ProductCategorySearchRequest { PageIndex = 0, PageSize = size, Keyword = " A?%_[~ B " });
        Assert.AreEqual(1, page.PageIndex);
        Assert.AreEqual(expectedSize, page.PageSize);
        Assert.IsNull(page.Items.Single().FakeName);
        Assert.IsNull(page.Items.Single().ParentId);
        Assert.AreEqual(2, fixture.Connection.Commands.Count);
        foreach (var command in fixture.Connection.Commands)
        {
            Assert.AreEqual("%%A_~%~_~[~~%B%%", command.Values["Keyword"]);
            Assert.AreEqual(expectedSize, command.Values["PageSize"]);
            Assert.AreEqual(0, command.Values["Offset"]);
            Assert.AreSame(fixture.Session.Transaction, command.Transaction);
            Assert.IsFalse(command.Sql.Contains("FOR UPDATE", StringComparison.Ordinal));
            StringAssert.Contains(command.Sql, "COALESCE(c.is_deleted,FALSE)=FALSE");
            StringAssert.Contains(command.Sql, "COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~'");
        }
        StringAssert.Contains(fixture.Connection.Text, "category_name COLLATE public.erp_dictionary_ci ASC NULLS FIRST");
        StringAssert.Contains(fixture.Connection.Text, "\"sort_no\" AS \"SortNo\"");
        fixture.Session.Transaction = null;
        await repository.GetPagedAsync(null!);
        Assert.AreEqual(20, fixture.Connection.Values["PageSize"]);
        Assert.IsFalse(fixture.Connection.Values.ContainsKey("Keyword"));
    }

    /// <summary>继承新增及更新仍绑定原版本和中文分类字段，数据库零行返回并发冲突。</summary>
    [TestMethod]
    public async Task CategoryCrud_PreservesVersionAndMappedParameters()
    {
        using var fixture = new Fixture();
        var repository = new PostgreSqlProductCategoryRepository(DictionaryContext(fixture));
        var entity = new ProductCategory { CategoryName = "分类", FakeName = null, CategoryCode = null, ParentId = null };
        Assert.IsTrue((await repository.AddAsync(entity)).Success);
        Assert.AreEqual("分类", fixture.Connection.Values["CategoryName"]);
        Assert.AreEqual(DBNull.Value, fixture.Connection.Values["ParentId"]);
        fixture.Connection.Rows.Columns.Add("Version", typeof(int));
        fixture.Connection.Rows.Rows.Add(4);
        entity.Version = 3;
        Assert.AreEqual(4, (await repository.UpdateAsync(entity)).NewVersion);
        Assert.AreEqual(3, fixture.Connection.Values["OriginalVersion"]);
        StringAssert.Contains(fixture.Connection.Text, "\"version\" = \"version\" + 1");
        fixture.Connection.Rows.Rows.Clear();
        Assert.AreEqual("CONCURRENCY_CONFLICT", (await repository.UpdateAsync(entity)).ErrorCode);
    }
}
