using Microsoft.VisualStudio.TestTools.UnitTesting;
using NpgsqlTypes;
using SwiftExport.Contracts.DTOs.Suppliers.Requests;
using SwiftExport.Core.Entities.Relations;
using SwiftExport.Infrastructure.Repositories.Base;
using SwiftExport.Infrastructure.Repositories.Suppliers.SupplierProducts;

namespace SwiftExport.Application.Tests.Database;

/// <summary>复用既有无网络 Dapper 夹具验证供应商产品参数、真实别名映射及外层事务，不修改共享夹具。</summary>
public sealed partial class PostgreSqlRepositoryTests
{
    /// <summary>详情 DTO 读取实时联表身份和可空价格，独立关联保留为空且不额外取得业务锁。</summary>
    [TestMethod]
    public async Task SupplierProduct_DetailMapsNullableProductAndDisplayValues()
    {
        using var f = new Fixture();
        var id = Guid.NewGuid();
        f.Connection.Rows.Columns.Add("Uid", typeof(Guid));
        f.Connection.Rows.Columns.Add("SkuId", typeof(Guid));
        f.Connection.Rows.Columns.Add("SupplierCode", typeof(string));
        f.Connection.Rows.Columns.Add("ProductSku", typeof(string));
        f.Connection.Rows.Columns.Add("UnitPrice", typeof(decimal));
        f.Connection.Rows.Columns.Add("IsProductDefaultSupplier", typeof(bool));
        f.Connection.Rows.Rows.Add(id, DBNull.Value, "实时供应商", DBNull.Value, DBNull.Value, false);
        var row = await new PostgreSqlSupplierProductRepository(DictionaryContext(f)).GetDtoByIdAsync(id);
        Assert.IsNotNull(row);
        Assert.AreEqual(id, row.Uid);
        Assert.IsNull(row.SkuId);
        Assert.IsNull(row.UnitPrice);
        Assert.AreEqual("实时供应商", row.SupplierCode);
        Assert.IsFalse(row.IsProductDefaultSupplier);
        StringAssert.Contains(f.Connection.Text, "p.sku AS \"ProductSku\"");
        StringAssert.Contains(f.Connection.Text, "LEFT JOIN public.product p");
        Assert.IsFalse(f.Connection.Text.Contains("is_active", StringComparison.Ordinal));
        Assert.IsFalse(f.Connection.Text.Contains("FOR UPDATE", StringComparison.Ordinal));
        Assert.AreSame(f.Session.Transaction, f.Connection.LastTransaction);
    }

    /// <summary>分页参数沿用默认和上限，关键词使用既有构造且不改变停用范围，所有语句共享事务。</summary>
    [TestMethod]
    [DataRow(0, 0, 1, 20)]
    [DataRow(2, 999, 2, 200)]
    public async Task SupplierProduct_PagePreservesLimitPatternAndTransaction(int index, int size, int expectedIndex, int expectedSize)
    {
        using var f = new Fixture();
        f.Connection.Rows.Columns.Add("SupplierSku", typeof(string));
        f.Connection.Rows.Rows.Add("原料号");
        var supplier = Guid.NewGuid();
        const string keyword = " a?~%_[\\ ";
        var page = await new PostgreSqlSupplierProductRepository(DictionaryContext(f)).GetPagedAsync(new SupplierProductSearchRequest
            { SupplierId = supplier, PageIndex = index, PageSize = size, Keyword = keyword });
        Assert.AreEqual(expectedIndex, page.PageIndex);
        Assert.AreEqual(expectedSize, page.PageSize);
        Assert.AreEqual(1, page.TotalCount);
        Assert.AreEqual("原料号", page.Items.Single().SupplierSku);
        Assert.HasCount(2, f.Connection.Commands);
        foreach (var command in f.Connection.Commands)
        {
            Assert.AreSame(f.Session.Transaction, command.Transaction);
            Assert.AreEqual(supplier, command.Values["SupplierId"]);
            Assert.AreEqual(SqlLikePattern.Build(keyword), command.Values["Keyword"]);
            StringAssert.Contains(command.Sql, "ESCAPE '~'");
            Assert.IsFalse(command.Sql.Contains("is_active", StringComparison.Ordinal));
        }
        StringAssert.Contains(f.Connection.Text, "sp.supplier_sku COLLATE public.erp_dictionary_ci ASC NULLS FIRST,sp.uid");
        Assert.AreEqual(expectedSize, f.Connection.Values["Limit"]);
        Assert.AreEqual((expectedIndex - 1) * expectedSize, f.Connection.Values["Offset"]);
    }

    /// <summary>编码集合不 Trim 或滤空，明确绑定文本/UUID 数组并排除整批身份，不锁定占用者。</summary>
    [TestMethod]
    public async Task SupplierProduct_CodeCollectionsPreserveInputAndExcludeBatchIds()
    {
        using var f = new Fixture();
        f.Connection.Rows.Columns.Add("SupplierSku", typeof(string));
        f.Connection.Rows.Rows.Add("Ab ");
        var repository = new PostgreSqlSupplierProductRepository(DictionaryContext(f));
        var supplier = Guid.NewGuid();
        Assert.AreEqual("Ab ", (await repository.GetExistingSupplierSkuListAsync([" ab ", ""], supplier)).Single());
        CollectionAssert.AreEqual(new[] { " ab ", "" }, (string[])f.Connection.Values["Codes"]!);
        Assert.AreEqual(NpgsqlDbType.Array | NpgsqlDbType.Text, f.Connection.Types["Codes"]);
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid() };
        Assert.AreEqual("Ab ", (await repository.GetSupplierSkusUsedByOtherRecordsAsync(supplier, [(ids[0], " ab "), (ids[1], "AB")])).Single());
        CollectionAssert.AreEqual(ids, (Guid[])f.Connection.Values["ExcludedIds"]!);
        Assert.AreEqual(NpgsqlDbType.Array | NpgsqlDbType.Uuid, f.Connection.Types["ExcludedIds"]);
        StringAssert.Contains(f.Connection.Text, "NOT (sp.uid=ANY(@ExcludedIds))");
        StringAssert.Contains(f.Connection.Text, "DISTINCT ON (rtrim(sp.supplier_sku,' ') COLLATE public.erp_dictionary_ci)");
        Assert.AreEqual(supplier, f.Connection.Values["SupplierId"]);
        Assert.IsTrue(f.Connection.Commands.All(c => ReferenceEquals(c.Transaction, f.Session.Transaction)));
        Assert.IsFalse(f.Connection.Text.Contains("FOR UPDATE", StringComparison.Ordinal));
    }

    /// <summary>料号 null/空输入不查询，其他 LINQ 集合 null 仍拒绝；空采购键过滤，供应商引用身份去重且不排除独立资料。</summary>
    [TestMethod]
    public async Task SupplierProduct_EmptyNullAndReferenceCollectionsHaveExplicitBoundaries()
    {
        using var f = new Fixture();
        var repository = new PostgreSqlSupplierProductRepository(DictionaryContext(f));
        Assert.HasCount(0, await repository.GetExistingSupplierSkuListAsync([], Guid.Empty));
        Assert.HasCount(0, await repository.GetSupplierSkusUsedByOtherRecordsAsync(Guid.Empty, []));
        Assert.HasCount(0, await repository.GetSupplierIdsWithProductsAsync([]));
        Assert.HasCount(0, await repository.GetPurchaseDataAsync([(Guid.Empty, Guid.NewGuid())]));
        Assert.HasCount(0, await repository.GetExistingSupplierSkuListAsync(null!, Guid.Empty));
        await Assert.ThrowsAsync<ArgumentNullException>(() => repository.GetSupplierSkusUsedByOtherRecordsAsync(Guid.Empty, null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => repository.GetSupplierIdsWithProductsAsync(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => repository.GetPurchaseDataAsync(null!));
        Assert.HasCount(0, f.Connection.Commands);
        var supplier = Guid.NewGuid();
        f.Connection.Rows.Columns.Add("SupplierId", typeof(Guid));
        f.Connection.Rows.Rows.Add(supplier);
        Assert.AreEqual(supplier, (await repository.GetSupplierIdsWithProductsAsync([supplier, supplier])).Single());
        CollectionAssert.AreEqual(new[] { supplier }, (Guid[])f.Connection.Values["Uids"]!);
        Assert.IsFalse(f.Connection.Text.Contains("sku_id", StringComparison.Ordinal));
    }

    /// <summary>采购键按 500 分块，两个 UUID 数组一一配对；不静默去重返回记录，完整实体别名覆盖编码快照和价格。</summary>
    [TestMethod]
    public async Task SupplierProduct_PurchasePairsChunkAndKeepReturnedRows()
    {
        using var f = new Fixture();
        f.Connection.Rows.Columns.Add("Uid", typeof(Guid));
        f.Connection.Rows.Columns.Add("SupplierSku", typeof(string));
        f.Connection.Rows.Columns.Add("UnitPrice", typeof(decimal));
        var id = Guid.NewGuid();
        f.Connection.Rows.Rows.Add(id, "采购快照", DBNull.Value);
        var keys = Enumerable.Range(0, 501).Select(_ => (ProductId: Guid.NewGuid(), SupplierId: Guid.NewGuid())).ToArray();
        var rows = await new PostgreSqlSupplierProductRepository(DictionaryContext(f)).GetPurchaseDataAsync(keys.Append(keys[0]));
        Assert.HasCount(2, f.Connection.Commands);
        Assert.HasCount(2, rows, "受控连接每块返回一行，仓储不得合并或择一返回记录。");
        Assert.IsTrue(rows.All(row => row.Uid == id && row.SupplierSku == "采购快照" && row.UnitPrice is null));
        for (var i = 0; i < 2; i++)
        {
            var command = f.Connection.Commands[i];
            CollectionAssert.AreEqual(keys.Skip(i * 500).Take(500).Select(k => k.ProductId).ToArray(), (Guid[])command.Values["ProductIds"]!);
            CollectionAssert.AreEqual(keys.Skip(i * 500).Take(500).Select(k => k.SupplierId).ToArray(), (Guid[])command.Values["SupplierIds"]!);
            StringAssert.Contains(command.Sql, "unnest(@ProductIds,@SupplierIds)");
            StringAssert.Contains(command.Sql, "sp.\"product_sku\" AS \"ProductSku\"");
            Assert.AreSame(f.Session.Transaction, command.Transaction);
        }
        Assert.AreEqual(0, f.Connection.Commits);
        Assert.AreEqual(0, f.Connection.Rollbacks);
    }

    /// <summary>继承 CRUD 显式保留 nullable SkuId 和原版本参数，不接管外层提交或增加供应商料号错误转换。</summary>
    [TestMethod]
    public async Task SupplierProduct_CrudPreservesNullableSkuAndVersion()
    {
        using var f = new Fixture();
        var repository = new PostgreSqlSupplierProductRepository(DictionaryContext(f));
        var entity = new SupplierProduct { SupplierId = Guid.NewGuid(), SupplierCode = "S", ProductSku = "", SupplierSku = "SP" };
        Assert.IsTrue((await repository.AddAsync(entity)).Success);
        Assert.AreEqual(DBNull.Value, f.Connection.Values["SkuId"]);
        Assert.AreEqual(DBNull.Value, f.Connection.Values["TaxPrice"]);
        f.Connection.Rows.Columns.Add("Version", typeof(int));
        f.Connection.Rows.Rows.Add(2);
        Assert.AreEqual(2, (await repository.UpdateAsync(entity)).NewVersion);
        Assert.AreEqual(1, f.Connection.Values["OriginalVersion"]);
        Assert.AreSame(f.Session.Transaction, f.Connection.LastTransaction);
        Assert.AreEqual(0, f.Connection.Commits);
        Assert.AreEqual(0, f.Connection.Rollbacks);
    }
}
