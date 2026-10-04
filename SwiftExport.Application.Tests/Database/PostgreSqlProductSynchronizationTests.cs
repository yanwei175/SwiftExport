using Microsoft.VisualStudio.TestTools.UnitTesting;
using NpgsqlTypes;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces.Products.Catalog;
using SwiftExport.Infrastructure.Repositories.Products.Catalog;

namespace SwiftExport.Application.Tests.Database;

/// <summary>复用离线命令夹具验证产品同步参数、精准差异及事务边界，不模拟实库更新。</summary>
public sealed partial class PostgreSqlRepositoryTests
{
    /// <summary>型号编码始终同步，两个选项控制描述配置和供应商；空 Guid 转 NULL，输入文本不再次修剪。</summary>
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public async Task ProductSync_SourcePreservesOptionsNullsAndExactText(bool descriptions, bool supplier)
    {
        using var f = new Fixture();
        var source = new ProductMaster { Uid = Guid.NewGuid(), MasterSku = " Ab ", DefaultDescriptionEn = null, DefaultFeaturesZh = "配置 " };
        Assert.AreEqual(1, await new PostgreSqlProductRepository(DictionaryContext(f)).SyncFromProductMasterAsync(source, descriptions, supplier));
        Assert.AreEqual(source.Uid, f.Connection.Values["ProductMasterId"]);
        Assert.AreEqual(" Ab ", f.Connection.Values["MasterSku"]);
        Assert.AreEqual(DBNull.Value, f.Connection.Values["DescriptionEn"]);
        Assert.AreEqual(DBNull.Value, f.Connection.Values["SupplierId"]);
        Assert.AreEqual("配置 ", f.Connection.Values["FeaturesZh"]);
        Assert.AreEqual(descriptions, f.Connection.Values["UpdateDescriptions"]);
        Assert.AreEqual(supplier, f.Connection.Values["UpdateDefaultSupplier"]);
        StringAssert.Contains(f.Connection.Text, "p.master_sku COLLATE \"C\" IS DISTINCT FROM");
        StringAssert.Contains(f.Connection.Text, "p.product_master_id=@ProductMasterId AND p.is_deleted=FALSE");
        Assert.IsFalse(f.Connection.Text.Contains("is_active", StringComparison.Ordinal));
        Assert.IsFalse(f.Connection.Text.Contains("FROM public.product_master", StringComparison.Ordinal));
        Assert.IsFalse(f.Connection.Text.Contains("erp_dictionary_ci", StringComparison.Ordinal));
        Assert.AreSame(f.Session.Transaction, f.Connection.LastTransaction);
        Assert.AreEqual(DateTimeKind.Unspecified, ((DateTime)f.Connection.Values["UpdatedDate"]!).Kind);
        Assert.AreEqual(NpgsqlDbType.Timestamp, f.Connection.Types["UpdatedDate"]);
        Assert.AreEqual(0, f.Connection.Commits);
    }

    /// <summary>两种集合入口只区别限定身份；四组旗标独立，保留有效父型号联接且不写型号编码。</summary>
    [TestMethod]
    [DataRow(false, 1)]
    [DataRow(false, 2)]
    [DataRow(false, 4)]
    [DataRow(false, 8)]
    [DataRow(false, 15)]
    [DataRow(true, 1)]
    [DataRow(true, 2)]
    [DataRow(true, 4)]
    [DataRow(true, 8)]
    [DataRow(true, 15)]
    public async Task ProductSync_SelectedGroupsKeepSourceAndTargetScope(bool byProduct, int flags)
    {
        using var f = new Fixture();
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.Empty };
        var fields = (ProductMasterSyncFields)flags;
        var repository = new PostgreSqlProductRepository(DictionaryContext(f));
        Assert.AreEqual(1, byProduct ? await repository.PullSelectedFieldsFromMastersAsync(ids, fields)
            : await repository.SyncSelectedFieldsFromMastersAsync(ids, fields));
        CollectionAssert.AreEqual(ids, (Guid[])f.Connection.Values["Uids"]!);
        Assert.AreEqual(NpgsqlDbType.Array | NpgsqlDbType.Uuid, f.Connection.Types["Uids"]);
        Assert.AreEqual(byProduct, f.Connection.Values["ByProduct"]);
        Assert.AreEqual(fields.HasFlag(ProductMasterSyncFields.Descriptions), f.Connection.Values["Descriptions"]);
        Assert.AreEqual(fields.HasFlag(ProductMasterSyncFields.Features), f.Connection.Values["Features"]);
        Assert.AreEqual(fields.HasFlag(ProductMasterSyncFields.Supplier), f.Connection.Values["Supplier"]);
        Assert.AreEqual(fields.HasFlag(ProductMasterSyncFields.WeightVolume), f.Connection.Values["WeightVolume"]);
        StringAssert.Contains(f.Connection.Text, "m.uid=p.product_master_id AND m.is_deleted=FALSE AND p.is_deleted=FALSE");
        StringAssert.Contains(f.Connection.Text, "NULLIF(m.default_supplier_id,@EmptyGuid)");
        StringAssert.Contains(f.Connection.Text, "p.unit_weight IS DISTINCT FROM m.default_gross_weight");
        StringAssert.Contains(f.Connection.Text, "p.unit_cbm IS DISTINCT FROM m.default_cbm");
        Assert.IsFalse(f.Connection.Text.Contains("master_sku=", StringComparison.Ordinal));
        Assert.IsFalse(f.Connection.Text.Contains("is_active", StringComparison.Ordinal));
        Assert.IsFalse(f.Connection.Text.Contains("FOR UPDATE", StringComparison.Ordinal));
        Assert.AreSame(f.Session.Transaction, f.Connection.LastTransaction);
    }

    /// <summary>原前置条件保留：三个同步入口要求事务，空集合和非法组选在事务检查之后拒绝，不发 SQL。</summary>
    [TestMethod]
    public async Task ProductSync_InvalidInputsNeverIssueCommands()
    {
        using var f = new Fixture();
        var repository = new PostgreSqlProductRepository(DictionaryContext(f));
        await Assert.ThrowsAsync<ArgumentNullException>(() => repository.SyncFromProductMasterAsync(null!, false, false));
        await Assert.ThrowsAsync<ArgumentException>(() => repository.SyncFromProductMasterAsync(new() { Uid = Guid.Empty }, false, false));
        await Assert.ThrowsAsync<ArgumentNullException>(() => repository.SyncSelectedFieldsFromMastersAsync(null!, ProductMasterSyncFields.All));
        await Assert.ThrowsAsync<ArgumentException>(() => repository.SyncSelectedFieldsFromMastersAsync([], ProductMasterSyncFields.All));
        await Assert.ThrowsAsync<ArgumentException>(() => repository.PullSelectedFieldsFromMastersAsync([Guid.NewGuid()], ProductMasterSyncFields.None));
        await Assert.ThrowsAsync<ArgumentException>(() => repository.PullSelectedFieldsFromMastersAsync([Guid.NewGuid()], (ProductMasterSyncFields)16));
        f.Session.Transaction!.Dispose();
        f.Session.Transaction = null;
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.SyncFromProductMasterAsync(new() { Uid = Guid.NewGuid() }, false, false));
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.SyncSelectedFieldsFromMastersAsync([], ProductMasterSyncFields.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.PullSelectedFieldsFromMastersAsync([Guid.NewGuid()], ProductMasterSyncFields.All));
        Assert.HasCount(0, f.Connection.Commands);
    }

    /// <summary>三个同步入口传递预取消，不执行命令或接管事务；专用供应商设置原契约没有取消参数。</summary>
    [TestMethod]
    public async Task ProductSync_PreCanceledMethodsDoNotExecuteOrOwnTransaction()
    {
        using var f = new Fixture();
        var repository = new PostgreSqlProductRepository(DictionaryContext(f));
        var token = new CancellationToken(true);
        var uid = Guid.NewGuid();
        await Assert.ThrowsAsync<OperationCanceledException>(() => repository.SyncFromProductMasterAsync(new() { Uid = uid }, true, true, token));
        await Assert.ThrowsAsync<OperationCanceledException>(() => repository.SyncSelectedFieldsFromMastersAsync([uid], ProductMasterSyncFields.All, token));
        await Assert.ThrowsAsync<OperationCanceledException>(() => repository.PullSelectedFieldsFromMastersAsync([uid], ProductMasterSyncFields.All, token));
        Assert.HasCount(0, f.Connection.Commands);
        Assert.AreEqual(0, f.Connection.Commits);
        Assert.AreEqual(0, f.Connection.Rollbacks);
    }

    /// <summary>专用供应商设置允许无事务且不转空 Guid，按旧版本递增一次，不引入实际差异过滤。</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ProductSync_SetSupplierKeepsOriginalVersionAndTransactionContract(bool withTransaction)
    {
        using var f = new Fixture();
        if (!withTransaction) { f.Session.Transaction!.Dispose(); f.Session.Transaction = null; }
        f.Connection.Rows.Columns.Add("version", typeof(int));
        f.Connection.Rows.Rows.Add(2);
        var uid = Guid.NewGuid();
        var repository = new PostgreSqlProductRepository(DictionaryContext(f));
        var result = await repository.SetDefaultSupplierAsync(uid, 1, Guid.Empty);
        Assert.AreEqual(2, result.NewVersion);
        Assert.AreEqual(1, result.OldVersion);
        Assert.AreEqual(Guid.Empty, f.Connection.Values["SupplierId"]);
        Assert.AreEqual(1, f.Connection.Values["ExpectedVersion"]);
        StringAssert.Contains(f.Connection.Text, "uid=@ProductId AND version=@ExpectedVersion AND is_deleted=FALSE");
        Assert.IsFalse(f.Connection.Text.Contains("IS DISTINCT FROM", StringComparison.Ordinal));
        Assert.AreEqual(NpgsqlDbType.Timestamp, f.Connection.Types["Now"]);
        Assert.AreSame(f.Session.Transaction, f.Connection.LastTransaction);
        f.Connection.Rows.Rows.Clear();
        Assert.AreEqual("CONCURRENCY_CONFLICT", (await repository.SetDefaultSupplierAsync(uid, 1, Guid.Empty)).ErrorCode);
        Assert.AreEqual(0, f.Connection.Commits);
        Assert.AreEqual(0, f.Connection.Rollbacks);
    }
}
