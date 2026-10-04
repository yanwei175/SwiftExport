using System.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.Products.ProductMasters.Requests;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Infrastructure.Repositories.Products.ProductMasters;

namespace SwiftExport.Application.Tests.Database;

/// <summary>复用既有离线夹具检查主型号查询投影、筛选、参数及版本写入，不接触数据库。</summary>
public sealed partial class PostgreSqlRepositoryTests
{
    /// <summary>组合分类与未分类筛选参数化；分页无上限，主图 LATERAL 稳定取一条，EXISTS 不重复主型号。</summary>
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public async Task MasterPage_UsesStableProjectionAndCategoryBranches(bool uncategorized, bool hasCategory)
    {
        using var fixture = new Fixture();
        fixture.Connection.Rows.Columns.Add("MasterSku", typeof(string));
        fixture.Connection.Rows.Columns.Add("CategoryName", typeof(string));
        fixture.Connection.Rows.Columns.Add("MainImagePath", typeof(string));
        fixture.Connection.Rows.Columns.Add("DefaultSalesPriceUsd", typeof(decimal));
        fixture.Connection.Rows.Rows.Add("M01", "分类", DBNull.Value, DBNull.Value);
        var category = Guid.NewGuid();
        var excluded = Guid.NewGuid();
        var repository = new PostgreSqlProductMasterRepository(DictionaryContext(fixture));
        var page = await repository.GetPagedAsync(new ProductMasterSearchRequest
        {
            Keyword = " A?%_[~ B ", PageIndex = 0, PageSize = 999, IncludeUncategorized = uncategorized,
            CategoryIds = hasCategory ? [Guid.Empty, category, category] : [Guid.Empty], ExcludeCategoryId = excluded
        });
        Assert.AreEqual(1, page.PageIndex);
        Assert.AreEqual(999, page.PageSize);
        Assert.AreEqual("M01", page.Items.Single().Data.MasterSku);
        Assert.IsNull(page.Items.Single().MainImagePath);
        Assert.IsNull(page.Items.Single().DefaultSalesPriceUsd);
        Assert.AreEqual(2, fixture.Connection.Commands.Count);
        foreach (var command in fixture.Connection.Commands)
        {
            Assert.AreEqual("%%A_~%~_~[~~%B%%", command.Values["Keyword"]);
            CollectionAssert.AreEqual(hasCategory ? new[] { category } : Array.Empty<Guid>(), (Guid[])command.Values["Uids"]!);
            Assert.AreEqual(excluded, command.Values["ExcludeCategoryId"]);
            Assert.AreEqual(999, command.Values["Limit"]);
            Assert.AreSame(fixture.Session.Transaction, command.Transaction);
            Assert.AreEqual(hasCategory, command.Sql.Contains("pm.category_id=ANY(@Uids)", StringComparison.Ordinal));
            Assert.AreEqual(uncategorized, command.Sql.Contains("OR pm.category_id IS NULL)", StringComparison.Ordinal));
            StringAssert.Contains(command.Sql, "pn.is_deleted=FALSE AND pn.product_code");
            Assert.IsFalse(command.Sql.Contains("FOR UPDATE", StringComparison.Ordinal));
        }
        StringAssert.Contains(fixture.Connection.Text, "LEFT JOIN LATERAL");
        StringAssert.Contains(fixture.Connection.Text, "pn.product_code COLLATE public.erp_dictionary_ci ASC NULLS FIRST,pn.uid LIMIT 1");
        StringAssert.Contains(fixture.Connection.Text, "\"default_features_en\" AS \"DefaultFeaturesEn\"");
        await repository.GetPagedAsync(null!);
        Assert.AreEqual(20, fixture.Connection.Values["Limit"], "空请求沿用 DTO 默认二十条。");
        await repository.GetPagedAsync(new ProductMasterSearchRequest { PageSize = 0 });
        Assert.AreEqual(50, fixture.Connection.Values["Limit"]);
    }

    /// <summary>详情还原空文本和完整主字段，锁仅在 pm；无调用方事务时拒绝锁查询。</summary>
    [TestMethod]
    public async Task MasterDetail_MapsNullsAndLocksOnlyMaster()
    {
        using var fixture = new Fixture();
        var uid = Guid.NewGuid();
        fixture.Connection.Rows.Columns.Add("Uid", typeof(Guid));
        fixture.Connection.Rows.Columns.Add("Version", typeof(int));
        fixture.Connection.Rows.Columns.Add("MasterSku", typeof(string));
        fixture.Connection.Rows.Columns.Add("CategoryName", typeof(string));
        fixture.Connection.Rows.Columns.Add("DefaultSupplierName", typeof(string));
        fixture.Connection.Rows.Columns.Add("DefaultFeaturesZh", typeof(string));
        fixture.Connection.Rows.Rows.Add(uid, 3, "M01", "未分类", DBNull.Value, DBNull.Value);
        var repository = new PostgreSqlProductMasterRepository(DictionaryContext(fixture));
        var detail = await repository.GetDetailHeaderAsync(uid, true);
        Assert.AreEqual(3, detail!.Version);
        Assert.AreEqual("M01", detail.Master.MasterSku);
        Assert.IsNull(detail.DefaultSupplierName);
        Assert.IsNull(detail.Master.DefaultFeaturesZh);
        StringAssert.Contains(fixture.Connection.Text, "FOR UPDATE OF pm");
        Assert.AreSame(fixture.Session.Transaction, fixture.Connection.LastTransaction);
        fixture.Session.Transaction!.Dispose();
        fixture.Session.Transaction = null;
        var count = fixture.Connection.Commands.Count;
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => repository.GetDetailHeaderAsync(uid, true));
        Assert.AreEqual(count, fixture.Connection.Commands.Count);
        await repository.GetDetailHeaderAsync(uid);
        Assert.IsFalse(fixture.Connection.Text.Contains("FOR UPDATE", StringComparison.Ordinal));
        fixture.Connection.Rows.Rows.Clear();
        Assert.IsNull(await repository.GetDetailHeaderAsync(uid));
    }

    /// <summary>编码输入 Trim 去重，PG单文本数组读取全部占用者；空集绕过锁前置而非创建事务。</summary>
    [TestMethod]
    public async Task MasterCodes_NormalizeInputsAndRequireCallerLockTransaction()
    {
        using var fixture = new Fixture();
        var repository = new PostgreSqlProductMasterRepository(DictionaryContext(fixture));
        await repository.GetByMasterSkusAsync([" M01 ", "m01", " ", "M02"], true);
        CollectionAssert.AreEqual(new[] { "M01", "M02" }, (string[])fixture.Connection.Values["Values"]!);
        StringAssert.Contains(fixture.Connection.Text, "unnest(@Values)");
        StringAssert.Contains(fixture.Connection.Text, "rtrim(pm.master_sku,' ') COLLATE public.erp_dictionary_ci");
        StringAssert.Contains(fixture.Connection.Text, "ORDER BY pm.uid FOR UPDATE OF pm");
        fixture.Session.Transaction!.Dispose();
        fixture.Session.Transaction = null;
        var count = fixture.Connection.Commands.Count;
        Assert.HasCount(0, await repository.GetByMasterSkusAsync([" ", ""], true));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => repository.GetByMasterSkusAsync(["M01"], true));
        Assert.AreEqual(count, fixture.Connection.Commands.Count);
    }

    /// <summary>归类绑定预期版本和本地审计时间，实际变化条件保留；未命中使用原并发错误。</summary>
    [TestMethod]
    public async Task MasterAssignCategory_ChangesOnlyCategoryAuditAndVersion()
    {
        using var fixture = new Fixture();
        fixture.Connection.Rows.Columns.Add("version", typeof(int));
        fixture.Connection.Rows.Rows.Add(4);
        var uid = Guid.NewGuid();
        var category = Guid.NewGuid();
        var repository = new PostgreSqlProductMasterRepository(DictionaryContext(fixture));
        var result = await repository.AssignCategoryAsync(uid, 3, category);
        Assert.IsTrue(result.Success);
        Assert.AreEqual(4, result.NewVersion);
        Assert.AreEqual(uid, fixture.Connection.Values["ProductMasterId"]);
        Assert.AreEqual(3, fixture.Connection.Values["ExpectedVersion"]);
        Assert.AreEqual(category, fixture.Connection.Values["TargetCategoryId"]);
        Assert.AreEqual(DateTimeKind.Unspecified, ((DateTime)fixture.Connection.Values["UpdatedDate"]!).Kind);
        StringAssert.Contains(fixture.Connection.Text, "category_id IS NULL OR category_id<>@TargetCategoryId");
        StringAssert.Contains(fixture.Connection.Text, "RETURNING version");
        Assert.IsFalse(fixture.Connection.Text.Contains("master_sku=", StringComparison.Ordinal));
        Assert.AreSame(fixture.Session.Transaction, fixture.Connection.LastTransaction);
        fixture.Connection.Rows.Rows.Clear();
        Assert.AreEqual("CONCURRENCY_CONFLICT", (await repository.AssignCategoryAsync(uid, 3, category)).ErrorCode);
    }

    /// <summary>所有直接接口传递取消，预取消不得执行查询或归类写入，也不得提交调用方事务。</summary>
    [TestMethod]
    public async Task MasterMethods_RespectCancellationWithoutOwningTransaction()
    {
        using var fixture = new Fixture();
        var repository = new PostgreSqlProductMasterRepository(DictionaryContext(fixture));
        var token = new CancellationToken(true);
        await Assert.ThrowsAsync<OperationCanceledException>(() => repository.GetPagedAsync(new(), token));
        await Assert.ThrowsAsync<OperationCanceledException>(() => repository.GetDetailHeaderAsync(Guid.NewGuid(), false, token));
        await Assert.ThrowsAsync<OperationCanceledException>(() => repository.GetByMasterSkusAsync(["M01"], false, token));
        await Assert.ThrowsAsync<OperationCanceledException>(() => repository.AssignCategoryAsync(Guid.NewGuid(), 1, Guid.NewGuid(), token));
        Assert.HasCount(0, fixture.Connection.Commands);
        Assert.AreEqual(0, fixture.Connection.Commits);
        Assert.AreEqual(0, fixture.Connection.Rollbacks);
    }

    /// <summary>继承 CRUD 绑定主型号实体，更新遵循版本并发契约且不恢复软删行。</summary>
    [TestMethod]
    public async Task MasterCrud_PreservesInheritedVersionContract()
    {
        using var fixture = new Fixture();
        var repository = new PostgreSqlProductMasterRepository(DictionaryContext(fixture));
        var master = new ProductMaster { MasterSku = "M01", DefaultDescriptionEn = null };
        Assert.IsTrue((await repository.AddAsync(master)).Success);
        Assert.AreEqual(DBNull.Value, fixture.Connection.Values["DefaultDescriptionEn"]);
        fixture.Connection.Rows.Columns.Add("Version", typeof(int));
        fixture.Connection.Rows.Rows.Add(2);
        Assert.IsTrue((await repository.UpdateAsync(master)).Success);
        Assert.AreEqual(1, fixture.Connection.Values["OriginalVersion"]);
        StringAssert.Contains(fixture.Connection.Text, "\"version\" = \"version\" + 1");
        fixture.Connection.Rows.Rows.Clear();
        Assert.AreEqual("CONCURRENCY_CONFLICT", (await repository.UpdateAsync(master)).ErrorCode);
    }
}
