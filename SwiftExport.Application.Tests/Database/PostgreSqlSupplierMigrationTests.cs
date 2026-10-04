using Microsoft.VisualStudio.TestTools.UnitTesting;
using NpgsqlTypes;
using SwiftExport.Contracts.DTOs.Suppliers.Requests;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces;
using SwiftExport.Infrastructure.Repositories.Base;
using SwiftExport.Infrastructure.Repositories.Suppliers.Management;

namespace SwiftExport.Application.Tests.Database;

/// <summary>复用公共无网络 Dapper 夹具验证供应商专用方法、参数投影与共享事务。</summary>
public sealed partial class PostgreSqlRepositoryTests
{
    /// <summary>单码入口保留空白及软删差异，实体完整映射停用状态和可空字段。</summary>
    [TestMethod]
    public async Task Supplier_SingleCodePreservesInputAndEntityProjection()
    {
        using var f = new Fixture();
        var repository = new PostgreSqlSupplierRepository(DictionaryContext(f));
        Assert.IsNull(await repository.GetByCodeAsync("  "));
        Assert.HasCount(0, f.Connection.Commands);
        f.Connection.Rows.Columns.Add("Uid", typeof(Guid));
        f.Connection.Rows.Columns.Add("SupplierCode", typeof(string));
        f.Connection.Rows.Columns.Add("IsActive", typeof(bool));
        f.Connection.Rows.Columns.Add("TaxRate", typeof(decimal));
        var uid = Guid.NewGuid();
        f.Connection.Rows.Rows.Add(uid, "Ab ", false, DBNull.Value);
        var entity = await repository.GetByCodeAsync("  ab  ");
        Assert.AreEqual(uid, entity!.Uid);
        Assert.IsFalse(entity.IsActive);
        Assert.IsNull(entity.TaxRate);
        Assert.AreEqual("ab", f.Connection.Values["Code"]);
        StringAssert.Contains(f.Connection.Text, "\"is_active\" AS \"IsActive\"");
        StringAssert.Contains(f.Connection.Text, "s.is_deleted = FALSE OR s.is_deleted IS NULL");
        Assert.IsTrue(await repository.ExistBySupplierCode(" ab "));
        Assert.AreEqual(" ab ", f.Connection.Values["Code"]);
        Assert.IsFalse(f.Connection.Text.Contains("is_deleted", StringComparison.Ordinal));
        Assert.AreSame(f.Session.Transaction, f.Connection.LastTransaction);
    }

    /// <summary>列表入口规范化但存在列表保持输入，数组比较包含软删且返回原文本。</summary>
    [TestMethod]
    public async Task Supplier_CodeCollectionsPreserveEachBoundary()
    {
        using var f = new Fixture();
        var repository = new PostgreSqlSupplierRepository(DictionaryContext(f));
        Assert.HasCount(0, await repository.GetBySupplierCodeListAsync(null!));
        Assert.HasCount(0, await repository.GetBySupplierCodeListAsync([" ", ""]));
        Assert.HasCount(0, await repository.GetExistingSupplierCodesAsync([]));
        Assert.HasCount(0, await repository.GetSupplierCodesUsedByOtherSuppliersAsync([]));
        Assert.HasCount(0, f.Connection.Commands);
        await repository.GetBySupplierCodeListAsync([" Ab ", "AB", " "]);
        CollectionAssert.AreEqual(new[] { "Ab" }, (string[])f.Connection.Values["Codes"]!);
        Assert.AreEqual(NpgsqlDbType.Array | NpgsqlDbType.Text, f.Connection.Types["Codes"]);
        StringAssert.Contains(f.Connection.Text, "EXISTS (SELECT 1 FROM unnest(@Codes)");
        StringAssert.Contains(f.Connection.Text, "rtrim(requested.code, ' ') COLLATE public.erp_dictionary_ci");
        f.Connection.Rows.Columns.Add("SupplierCode", typeof(string));
        f.Connection.Rows.Rows.Add("Ab ");
        Assert.AreEqual("Ab ", (await repository.GetExistingSupplierCodesAsync([" ab "])).Single());
        CollectionAssert.AreEqual(new[] { " ab " }, (string[])f.Connection.Values["Codes"]!);
        Assert.IsFalse(f.Connection.Text.Contains("is_deleted", StringComparison.Ordinal));
        var uid = Guid.NewGuid();
        Assert.AreEqual("Ab ", (await repository.GetSupplierCodesUsedByOtherSuppliersAsync([(uid, "AB")])).Single());
        CollectionAssert.AreEqual(new[] { uid }, (Guid[])f.Connection.Values["Uids"]!);
        StringAssert.Contains(f.Connection.Text, "DISTINCT ON (rtrim(s.supplier_code, ' ') COLLATE public.erp_dictionary_ci)");
        Assert.IsFalse(f.Connection.Text.Contains("is_deleted", StringComparison.Ordinal));
        Assert.IsTrue(f.Connection.Commands.All(c => ReferenceEquals(c.Transaction, f.Session.Transaction)));
        Assert.AreEqual(0, f.Connection.Commits);
        Assert.AreEqual(0, f.Connection.Rollbacks);
    }

    /// <summary>分页保留原 DTO 的可空数值、通配构造、默认页与上限，不附加启用状态过滤。</summary>
    [TestMethod]
    [DataRow(0, 0, 1, 20)]
    [DataRow(2, 999, 2, 200)]
    public async Task Supplier_PagePreservesProjectionAndWildcard(int index, int size, int expectedIndex, int expectedSize)
    {
        using var f = new Fixture();
        f.Connection.Rows.Columns.Add("SupplierCode", typeof(string));
        f.Connection.Rows.Columns.Add("ZheKou", typeof(decimal));
        f.Connection.Rows.Rows.Add("原码", DBNull.Value);
        const string keyword = " a*?~%_[\\ ";
        var page = await new PostgreSqlSupplierRepository(DictionaryContext(f)).GetPagedAsync(
            new SupplierSearchRequest { PageIndex = index, PageSize = size, Keyword = keyword, BatchKeywords = ["忽略"] });
        Assert.AreEqual(expectedIndex, page.PageIndex);
        Assert.AreEqual(expectedSize, page.PageSize);
        Assert.AreEqual(1, page.TotalCount);
        Assert.AreEqual("原码", page.Items.Single().SupplierCode);
        Assert.IsNull(page.Items.Single().ZheKou);
        Assert.HasCount(2, f.Connection.Commands);
        foreach (var command in f.Connection.Commands)
        {
            Assert.AreSame(f.Session.Transaction, command.Transaction);
            Assert.AreEqual(SqlLikePattern.Build(keyword), command.Values["Keyword"]);
            StringAssert.Contains(command.Sql, "ESCAPE '~'");
            Assert.IsFalse(command.Sql.Contains("is_active", StringComparison.Ordinal));
            Assert.IsFalse(command.Sql.Contains("BatchKeywords", StringComparison.Ordinal));
        }
        StringAssert.Contains(f.Connection.Text, "s.supplier_code COLLATE public.erp_dictionary_ci, s.uid");
        Assert.AreEqual((expectedIndex - 1) * expectedSize, f.Connection.Values["Offset"]);
    }

    /// <summary>继承更新仍使用版本条件和公共参数，明确排除 IsActive 且可空实体字段绑定 DBNull。</summary>
    [TestMethod]
    public async Task Supplier_UpdateExcludesLifecycleAndPreservesVersion()
    {
        using var f = new Fixture();
        f.Connection.Rows.Columns.Add("Version", typeof(int));
        f.Connection.Rows.Rows.Add(4);
        var entity = new Supplier { Uid = Guid.NewGuid(), Version = 3, SupplierCode = "A", SupplierName = "供应商", DocRelateNo = "A", IsActive = false, TaxRate = null };
        var result = await new PostgreSqlSupplierRepository(DictionaryContext(f)).UpdateAsync(entity);
        Assert.AreEqual(4, result.NewVersion);
        StringAssert.Contains(f.Connection.Text, "\"version\" = @OriginalVersion");
        Assert.IsFalse(f.Connection.Text.Contains("\"is_active\" =", StringComparison.Ordinal));
        Assert.AreEqual(DBNull.Value, f.Connection.Values["TaxRate"]);
        Assert.AreSame(f.Session.Transaction, f.Connection.LastTransaction);
        Assert.AreEqual(0, f.Connection.Commits);
    }

    /// <summary>空分页请求沿用默认值，空关键词不创建参数或搜索条件。</summary>
    [TestMethod]
    public async Task Supplier_DefaultPageDoesNotBindUnusedKeyword()
    {
        using var f = new Fixture();
        var page = await new PostgreSqlSupplierRepository(DictionaryContext(f)).GetPagedAsync(null!);
        Assert.AreEqual(1, page.PageIndex);
        Assert.AreEqual(20, page.PageSize);
        Assert.HasCount(2, f.Connection.Commands);
        foreach (var command in f.Connection.Commands)
        {
            Assert.IsFalse(command.Values.ContainsKey("Keyword"));
            Assert.IsFalse(command.Sql.Contains("LIKE", StringComparison.Ordinal));
            Assert.AreSame(f.Session.Transaction, command.Transaction);
        }
    }
}
