using Microsoft.VisualStudio.TestTools.UnitTesting;
using NpgsqlTypes;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Infrastructure.Repositories.Base;
using SwiftExport.Infrastructure.Repositories.Products.Colors;

namespace SwiftExport.Application.Tests.Database;

/// <summary>复用公共离线夹具，验证颜色方法的投影、软删、数组和最小锁边界，不模拟数据库竞争。</summary>
public sealed partial class PostgreSqlRepositoryTests
{
    /// <summary>存在及占用列表保留原输入和软删范围，普通查询无锁且原码不归一化返回。</summary>
    [TestMethod]
    public async Task Color_ExistAndExistingKeepOriginalCodesAndDeletedScope()
    {
        using var f = new Fixture();
        var repository = new PostgreSqlProductColorRepository(DictionaryContext(f));
        Assert.IsTrue(await repository.ExistByColorCode(" Ab "));
        Assert.AreEqual(" Ab ", f.Connection.Values["Code"]);
        Assert.IsFalse(f.Connection.Text.Contains("is_deleted", StringComparison.Ordinal));
        f.Connection.Rows.Columns.Add("ColorCodeUnique", typeof(string));
        f.Connection.Rows.Rows.Add("Ab ");
        Assert.AreEqual("Ab ", (await repository.GetExistingColorCodesAsync([" ab ", " ab "])).Single());
        CollectionAssert.AreEqual(new[] { " ab ", " ab " }, (string[])f.Connection.Values["Codes"]!);
        Assert.AreEqual(NpgsqlDbType.Array | NpgsqlDbType.Text, f.Connection.Types["Codes"]);
        StringAssert.Contains(f.Connection.Text, "EXISTS (SELECT 1 FROM unnest(@Codes)");
        Assert.IsFalse(f.Connection.Text.Contains("is_deleted", StringComparison.Ordinal));
        Assert.IsTrue(f.Connection.Commands.All(c => !c.Sql.Contains("FOR UPDATE", StringComparison.Ordinal)
            && ReferenceEquals(c.Transaction, f.Session.Transaction)));
    }

    /// <summary>编码读取保留窄投影、可空名称和严格未删除条件，仅显式锁入口申请行锁。</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Color_CodeLookupKeepsNarrowProjectionAndOptionalLock(bool locking)
    {
        using var f = new Fixture();
        f.Connection.Rows.Columns.Add("Uid", typeof(Guid));
        f.Connection.Rows.Columns.Add("Version", typeof(int));
        f.Connection.Rows.Columns.Add("ColorNameZh", typeof(string));
        var uid = Guid.NewGuid();
        f.Connection.Rows.Rows.Add(uid, 3, DBNull.Value);
        var result = await new PostgreSqlProductColorRepository(DictionaryContext(f)).GetByColorCodesAsync([" Ab ", "AB", " "], locking);
        Assert.AreEqual(uid, result.Single().Uid);
        Assert.AreEqual(3, result.Single().Version);
        Assert.IsNull(result.Single().ColorNameZh);
        CollectionAssert.AreEqual(new[] { "Ab" }, (string[])f.Connection.Values["Codes"]!);
        StringAssert.Contains(f.Connection.Text, locking
            ? "c.is_deleted=FALSE ORDER BY c.uid"
            : "c.is_deleted=FALSE ORDER BY c.sort_no ASC, c.color_code_unique COLLATE public.erp_dictionary_ci ASC NULLS FIRST, c.uid");
        Assert.AreEqual(locking, f.Connection.Text.Contains("FOR UPDATE", StringComparison.Ordinal));
        Assert.IsFalse(f.Connection.Text.Contains("CreatedDate", StringComparison.Ordinal));
        Assert.AreSame(f.Session.Transaction, f.Connection.LastTransaction);
    }

    /// <summary>空输入不查询也不要求事务；非空锁入口必须已有外层事务，普通读取允许自动提交。</summary>
    [TestMethod]
    public async Task Color_EmptyAndTransactionRequirementsStayMinimal()
    {
        using var f = new Fixture();
        f.Session.Transaction!.Dispose();
        f.Session.Transaction = null;
        var repository = new PostgreSqlProductColorRepository(DictionaryContext(f));
        Assert.HasCount(0, await repository.GetByColorCodesAsync(null!, true));
        Assert.HasCount(0, await repository.GetReferencedColorIdsAsync([Guid.Empty]));
        Assert.HasCount(0, await repository.GetExistingColorCodesAsync([]));
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.GetByColorCodesAsync(["A"], true));
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.GetReferencedColorIdsAsync([Guid.NewGuid()]));
        Assert.HasCount(0, f.Connection.Commands);
        await repository.GetByColorCodesAsync(["A"]);
        Assert.IsNull(f.Connection.LastTransaction);
        Assert.IsFalse(f.Connection.Text.Contains("FOR UPDATE", StringComparison.Ordinal));
    }

    /// <summary>保留产品现存行锁，中性引用在父颜色协调下只读，不重复锁子行，最终在内存去重。</summary>
    [TestMethod]
    public async Task Color_ReferencesLockProductRowsBeforeDeduplication()
    {
        using var f = new Fixture();
        var uid = Guid.NewGuid();
        f.Connection.Rows.Columns.Add("ColorId", typeof(Guid));
        f.Connection.Rows.Rows.Add(uid);
        f.Connection.Rows.Rows.Add(uid);
        var result = await new PostgreSqlProductColorRepository(DictionaryContext(f)).GetReferencedColorIdsAsync([uid, uid, Guid.Empty]);
        CollectionAssert.AreEqual(new[] { uid }, result.ToArray());
        CollectionAssert.AreEqual(new[] { uid }, (Guid[])f.Connection.Values["Uids"]!);
        Assert.HasCount(2, f.Connection.Commands);
        StringAssert.Contains(f.Connection.Commands[0].Sql, "\"public\".\"product\" p");
        StringAssert.Contains(f.Connection.Commands[0].Sql, "p.is_deleted=FALSE ORDER BY p.uid FOR UPDATE");
        StringAssert.Contains(f.Connection.Commands[1].Sql, "pn.is_deleted=FALSE ORDER BY pn.uid");
        Assert.IsFalse(f.Connection.Commands[1].Sql.Contains("FOR UPDATE", StringComparison.Ordinal));
        Assert.IsFalse(f.Connection.Text.Contains("DISTINCT", StringComparison.Ordinal));
        Assert.IsTrue(f.Connection.Commands.Any(c => c.Sql.Contains("product_neutral", StringComparison.Ordinal)),
            "有效中性产品引用也必须阻止删除颜色。");
        Assert.AreSame(f.Session.Transaction, f.Connection.LastTransaction);
        Assert.AreEqual(0, f.Connection.Commits);
    }

    /// <summary>默认页五十且不增加上限，批量关键词规范化，保留原通配构造、NULL 排序及完整投影。</summary>
    [TestMethod]
    [DataRow(0, 0, 1, 50)]
    [DataRow(2, 501, 2, 501)]
    public async Task Color_PagePreservesBatchWildcardAndLimits(int index, int size, int expectedIndex, int expectedSize)
    {
        using var f = new Fixture();
        f.Connection.Rows.Columns.Add("ColorNameEn", typeof(string));
        f.Connection.Rows.Columns.Add("CreatedBy", typeof(string));
        f.Connection.Rows.Rows.Add(DBNull.Value, "原审计");
        const string keyword = " A? ~%_[\\ ";
        var result = await new PostgreSqlProductColorRepository(DictionaryContext(f)).GetProductColorPagedAsync(
            new ProductColorSearchRequest { PageIndex = index, PageSize = size, Keyword = keyword, BatchKeywords = [" Ab ", "AB", " "] });
        Assert.AreEqual(expectedIndex, result.PageIndex);
        Assert.AreEqual(expectedSize, result.PageSize);
        Assert.IsNull(result.Items.Single().ColorNameEn);
        Assert.AreEqual("原审计", result.Items.Single().CreatedBy);
        Assert.HasCount(2, f.Connection.Commands);
        foreach (var command in f.Connection.Commands)
        {
            Assert.AreEqual(SqlLikePattern.Build(keyword), command.Values["Keyword"]);
            CollectionAssert.AreEqual(new[] { "Ab" }, (string[])command.Values["BatchKeywords"]!);
            StringAssert.Contains(command.Sql, "c.is_deleted=FALSE OR c.is_deleted IS NULL");
            StringAssert.Contains(command.Sql, "ESCAPE '~'");
            Assert.IsFalse(command.Sql.Contains("FOR UPDATE", StringComparison.Ordinal));
            Assert.AreSame(f.Session.Transaction, command.Transaction);
        }
        StringAssert.Contains(f.Connection.Text, "ASC NULLS FIRST, c.uid");
    }

    /// <summary>继承 CRUD 使用公共映射和版本条件，更新可空名称不建立或提交业务事务。</summary>
    [TestMethod]
    public async Task Color_CrudPreservesVersionAndNullParameters()
    {
        using var f = new Fixture();
        var repository = new PostgreSqlProductColorRepository(DictionaryContext(f));
        var entity = new ProductColor { ColorCodeUnique = "A", ColorCode = "A", ColorNameZh = null, SortNo = 8 };
        Assert.IsTrue((await repository.AddAsync(entity)).Success);
        Assert.AreEqual(1, entity.Version);
        Assert.AreEqual(DBNull.Value, f.Connection.Values["ColorNameZh"]);
        Assert.AreEqual(8, f.Connection.Values["SortNo"]);
        f.Connection.Rows.Columns.Add("Version", typeof(int));
        f.Connection.Rows.Rows.Add(2);
        Assert.AreEqual(2, (await repository.UpdateAsync(entity)).NewVersion);
        StringAssert.Contains(f.Connection.Text, "\"version\" = @OriginalVersion");
        Assert.AreSame(f.Session.Transaction, f.Connection.LastTransaction);
        Assert.AreEqual(0, f.Connection.Commits);
        Assert.AreEqual(0, f.Connection.Rollbacks);
    }

    /// <summary>全量颜色读取投影排序号并保持全局稳定顺序、软删范围和当前事务。</summary>
    [TestMethod]
    public async Task Color_AllReadsSortNoAndUsesStableGlobalOrder()
    {
        using var f = new Fixture();
        f.Connection.Rows.Columns.Add("SortNo", typeof(int));
        f.Connection.Rows.Rows.Add(7);
        var result = await new PostgreSqlProductColorRepository(DictionaryContext(f)).GetAllAsync();
        Assert.AreEqual(7, result.Single().SortNo);
        StringAssert.Contains(f.Connection.Text, "c.\"sort_no\" AS \"SortNo\"");
        StringAssert.Contains(f.Connection.Text, "ORDER BY c.sort_no ASC, c.color_code_unique COLLATE public.erp_dictionary_ci ASC NULLS FIRST, c.uid");
        StringAssert.Contains(f.Connection.Text, "c.is_deleted=FALSE OR c.is_deleted IS NULL");
        Assert.IsFalse(f.Connection.Text.Contains("FOR UPDATE", StringComparison.Ordinal));
        Assert.AreSame(f.Session.Transaction, f.Connection.LastTransaction);
    }
}
