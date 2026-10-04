using Microsoft.VisualStudio.TestTools.UnitTesting;
using NpgsqlTypes;
using SwiftExport.Contracts.DTOs.Products.ProductNeutralManager.Requests;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Infrastructure.Repositories.Base;
using SwiftExport.Infrastructure.Repositories.Products.ProductNeutrals;

namespace SwiftExport.Application.Tests.Database;

/// <summary>复用离线 Dapper 夹具验证中性产品管理投影、数组筛选及最小事务锁边界。</summary>
public sealed partial class PostgreSqlRepositoryTests
{
    /// <summary>分页默认、回退和大页不被限流；分类与未分类同组，颜色及关键词同时筛选，投影保留审计及可空价格。</summary>
    [TestMethod]
    [DataRow(0, 0, 1, 50)]
    [DataRow(2, 5001, 2, 5001)]
    public async Task NeutralManager_PagePreservesFiltersAndProjection(int index, int size, int expectedIndex, int expectedSize)
    {
        using var f = new Fixture();
        var category = Guid.NewGuid();
        var color = Guid.NewGuid();
        var uid = Guid.NewGuid();
        f.Connection.Rows.Columns.Add("Uid", typeof(Guid));
        f.Connection.Rows.Columns.Add("Version", typeof(int));
        f.Connection.Rows.Columns.Add("ProductCode", typeof(string));
        f.Connection.Rows.Columns.Add("DefaultSalesPriceUsd", typeof(decimal));
        f.Connection.Rows.Columns.Add("IsMainImage", typeof(bool));
        f.Connection.Rows.Columns.Add("MasterSku", typeof(string));
        f.Connection.Rows.Columns.Add("ColorName", typeof(string));
        f.Connection.Rows.Columns.Add("CreatedBy", typeof(string));
        f.Connection.Rows.Rows.Add(uid, 3, "原码 ", DBNull.Value, true, "M", DBNull.Value, "审计");
        const string keyword = " A? ~%_[\\ ";
        var page = await new PostgreSqlProductNeutralManagerRepository(DictionaryContext(f)).GetPagedAsync(new()
        {
            PageIndex = index, PageSize = size, Keyword = keyword, IncludeUncategorized = true,
            CategoryIds = [Guid.Empty, category, category], ColorIds = [color, Guid.Empty, color]
        });
        Assert.AreEqual(expectedIndex, page.PageIndex);
        Assert.AreEqual(expectedSize, page.PageSize);
        Assert.AreEqual(uid, page.Items.Single().Uid);
        Assert.AreEqual(3, page.Items.Single().Version);
        Assert.AreEqual("原码 ", page.Items.Single().Data.ProductCode);
        Assert.IsNull(page.Items.Single().Data.DefaultSalesPriceUsd);
        Assert.IsNull(page.Items.Single().ColorName);
        Assert.AreEqual("审计", page.Items.Single().CreatedBy);
        Assert.HasCount(2, f.Connection.Commands);
        foreach (var command in f.Connection.Commands)
        {
            StringAssert.Contains(command.Sql, "INNER JOIN \"public\".\"product_master\"");
            StringAssert.Contains(command.Sql, "pn.is_deleted=FALSE AND pm.is_deleted=FALSE");
            StringAssert.Contains(command.Sql, "(pm.category_id=ANY(@CategoryIds) OR pm.category_id=@EmptyGuid OR pm.category_id IS NULL)");
            StringAssert.Contains(command.Sql, "pn.color_id=ANY(@ColorIds)");
            CollectionAssert.AreEqual(new[] { category }, (Guid[])command.Values["CategoryIds"]!);
            CollectionAssert.AreEqual(new[] { color }, (Guid[])command.Values["ColorIds"]!);
            Assert.AreEqual(SqlLikePattern.Build(keyword), command.Values["Keyword"]);
            Assert.IsFalse(command.Sql.Contains("FOR UPDATE", StringComparison.Ordinal));
            Assert.IsFalse(command.Sql.Contains("SELECT *", StringComparison.Ordinal));
            Assert.AreSame(f.Session.Transaction, command.Transaction);
        }
        Assert.AreEqual((expectedIndex - 1) * expectedSize, f.Connection.Values["Offset"]);
        StringAssert.Contains(f.Connection.Text, "pn.product_code COLLATE public.erp_dictionary_ci ASC NULLS FIRST, pn.uid");
        StringAssert.Contains(f.Connection.Text, "rtrim(color.color_name_zh, ' ') COLLATE \"C\" = ''");
        StringAssert.Contains(f.Connection.Text, "color.color_name_en, color.color_code");
        StringAssert.Contains(f.Connection.Text, "\"is_main_image\" AS \"IsMainImage\"");
    }

    /// <summary>未分类、真实分类和无筛选保持各自分支；请求默认页大小来自现有 DTO，普通分页允许无事务。</summary>
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    public async Task NeutralManager_PageKeepsCategoryBranches(bool uncategorized, bool hasCategory)
    {
        using var f = new Fixture();
        f.Session.Transaction!.Dispose();
        f.Session.Transaction = null;
        var page = await new PostgreSqlProductNeutralManagerRepository(DictionaryContext(f)).GetPagedAsync(new()
        { IncludeUncategorized = uncategorized, CategoryIds = hasCategory ? [Guid.NewGuid()] : [Guid.Empty] });
        Assert.AreEqual(new ProductNeutralManagerSearchRequest().PageSize, page.PageSize);
        Assert.AreEqual(hasCategory, f.Connection.Text.Contains("pm.category_id=ANY(@CategoryIds)", StringComparison.Ordinal));
        Assert.AreEqual(uncategorized, f.Connection.Text.Contains("pm.category_id IS NULL", StringComparison.Ordinal));
        Assert.IsFalse(f.Connection.Text.Contains("pn.color_id=ANY", StringComparison.Ordinal));
        Assert.IsNull(f.Connection.LastTransaction);
        Assert.IsFalse(f.Connection.Text.Contains("FOR UPDATE", StringComparison.Ordinal));
    }

    /// <summary>占用查询修剪去重输入而保留原码、主图及完整实体字段，不按父型号范围过滤或锁父表。</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task NeutralManager_CodeLookupLocksOnlyMatchedNeutralRows(bool locking)
    {
        using var f = new Fixture();
        f.Connection.Rows.Columns.Add("ProductCode", typeof(string));
        f.Connection.Rows.Columns.Add("IsMainImage", typeof(bool));
        f.Connection.Rows.Rows.Add("Ab ", true);
        var rows = await new PostgreSqlProductNeutralManagerRepository(DictionaryContext(f))
            .GetByProductCodesAsync([" Ab ", "AB", " ", ""], locking);
        Assert.AreEqual("Ab ", rows.Single().ProductCode);
        Assert.IsTrue(rows.Single().IsMainImage);
        CollectionAssert.AreEqual(new[] { "Ab" }, (string[])f.Connection.Values["Values"]!);
        Assert.AreEqual(NpgsqlDbType.Array | NpgsqlDbType.Text, f.Connection.Types["Values"]);
        StringAssert.Contains(f.Connection.Text, "pn.is_deleted=FALSE AND EXISTS");
        StringAssert.Contains(f.Connection.Text, "rtrim(pn.product_code, ' ') COLLATE public.erp_dictionary_ci");
        StringAssert.Contains(f.Connection.Text, "ORDER BY pn.uid");
        Assert.AreEqual(locking, f.Connection.Text.EndsWith(" FOR UPDATE", StringComparison.Ordinal));
        Assert.IsFalse(f.Connection.Text.Contains("JOIN", StringComparison.Ordinal));
        Assert.AreSame(f.Session.Transaction, f.Connection.LastTransaction);
    }

    /// <summary>空集合不访问数据库或要求事务，非空锁入口拒绝无事务；普通编码查询可自动提交读取。</summary>
    [TestMethod]
    public async Task NeutralManager_EmptyAndTransactionRequirementsStayMinimal()
    {
        using var f = new Fixture();
        f.Session.Transaction!.Dispose();
        f.Session.Transaction = null;
        var repository = new PostgreSqlProductNeutralManagerRepository(DictionaryContext(f));
        Assert.HasCount(0, await repository.GetByProductCodesAsync([" "], true));
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.GetByProductCodesAsync(["A"], true));
        Assert.HasCount(0, f.Connection.Commands);
        await repository.GetByProductCodesAsync(["A"]);
        Assert.IsNull(f.Connection.LastTransaction);
        Assert.IsFalse(f.Connection.Text.Contains("FOR UPDATE", StringComparison.Ordinal));
    }

    /// <summary>继承 CRUD 保留主图、价格 NULL 和乐观版本，不拥有事务或额外业务字段规则。</summary>
    [TestMethod]
    public async Task NeutralManager_CrudPreservesVersionMainImageAndNulls()
    {
        using var f = new Fixture();
        var repository = new PostgreSqlProductNeutralManagerRepository(DictionaryContext(f));
        var entity = new ProductNeutral { ProductCode = "A", ProductMasterId = Guid.NewGuid(), ColorId = Guid.NewGuid(), IsMainImage = true };
        Assert.IsTrue((await repository.AddAsync(entity)).Success);
        Assert.AreEqual(DBNull.Value, f.Connection.Values["DefaultSalesPriceUsd"]);
        Assert.AreEqual(true, f.Connection.Values["IsMainImage"]);
        f.Connection.Rows.Columns.Add("Version", typeof(int));
        f.Connection.Rows.Rows.Add(2);
        Assert.AreEqual(2, (await repository.UpdateAsync(entity)).NewVersion);
        StringAssert.Contains(f.Connection.Text, "\"version\" = @OriginalVersion");
        Assert.AreEqual(true, f.Connection.Values["IsMainImage"]);
        Assert.IsTrue((await repository.DeleteAsync(entity.Uid, 2)).Success);
        Assert.AreSame(f.Session.Transaction, f.Connection.LastTransaction);
        Assert.AreEqual(0, f.Connection.Commits);
        Assert.AreEqual(0, f.Connection.Rollbacks);
    }
}
