using Microsoft.VisualStudio.TestTools.UnitTesting;
using NpgsqlTypes;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces.Products.Catalog;
using SwiftExport.Infrastructure.Repositories.Products.Catalog;

namespace SwiftExport.Application.Tests.Database;

/// <summary>复用离线命令夹具验证物料获取参数、精准差异及事务边界，不模拟实库更新。</summary>
public sealed partial class PostgreSqlRepositoryTests
{
    /// <summary>仅物料选集限定目标；四组旗标独立，保留有效父型号联接且不写型号编码。</summary>
    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(4)]
    [DataRow(8)]
    [DataRow(15)]
    public async Task ProductPull_SelectedGroupsKeepSourceAndTargetScope(int flags)
    {
        using var f = new Fixture();
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.Empty };
        var fields = (ProductMasterSyncFields)flags;
        var repository = new PostgreSqlProductRepository(DictionaryContext(f));
        Assert.AreEqual(1, await repository.PullSelectedFieldsFromMastersAsync(ids, fields));
        CollectionAssert.AreEqual(ids, (Guid[])f.Connection.Values["Uids"]!);
        Assert.AreEqual(NpgsqlDbType.Array | NpgsqlDbType.Uuid, f.Connection.Types["Uids"]);
        Assert.IsFalse(f.Connection.Values.ContainsKey("ByProduct"));
        Assert.AreEqual(fields.HasFlag(ProductMasterSyncFields.Descriptions), f.Connection.Values["Descriptions"]);
        Assert.AreEqual(fields.HasFlag(ProductMasterSyncFields.Features), f.Connection.Values["Features"]);
        Assert.AreEqual(fields.HasFlag(ProductMasterSyncFields.Supplier), f.Connection.Values["Supplier"]);
        Assert.AreEqual(fields.HasFlag(ProductMasterSyncFields.WeightVolume), f.Connection.Values["WeightVolume"]);
        StringAssert.Contains(f.Connection.Text, "m.uid=p.product_master_id AND m.is_deleted=FALSE AND p.is_deleted=FALSE");
        StringAssert.Contains(f.Connection.Text, "AND p.uid=ANY(@Uids)");
        Assert.IsFalse(f.Connection.Text.Contains("m.uid=ANY(@Uids)", StringComparison.Ordinal));
        StringAssert.Contains(f.Connection.Text, "NULLIF(m.default_supplier_id,@EmptyGuid)");
        StringAssert.Contains(f.Connection.Text, "p.unit_weight IS DISTINCT FROM m.default_gross_weight");
        StringAssert.Contains(f.Connection.Text, "p.unit_cbm IS DISTINCT FROM m.default_cbm");
        Assert.IsFalse(f.Connection.Text.Contains("master_sku=", StringComparison.Ordinal));
        Assert.IsFalse(f.Connection.Text.Contains("is_active", StringComparison.Ordinal));
        Assert.IsFalse(f.Connection.Text.Contains("FOR UPDATE", StringComparison.Ordinal));
        Assert.AreSame(f.Session.Transaction, f.Connection.LastTransaction);
        Assert.AreEqual(DateTimeKind.Unspecified, ((DateTime)f.Connection.Values["UpdatedDate"]!).Kind);
        Assert.AreEqual(NpgsqlDbType.Timestamp, f.Connection.Types["UpdatedDate"]);
        Assert.AreEqual(0, f.Connection.Commits);
    }

    /// <summary>拉取要求事务，空集合和非法组选在事务检查之后拒绝，不发 SQL。</summary>
    [TestMethod]
    public async Task ProductSync_InvalidInputsNeverIssueCommands()
    {
        using var f = new Fixture();
        var repository = new PostgreSqlProductRepository(DictionaryContext(f));
        await Assert.ThrowsAsync<ArgumentNullException>(() => repository.PullSelectedFieldsFromMastersAsync(null!, ProductMasterSyncFields.All));
        await Assert.ThrowsAsync<ArgumentException>(() => repository.PullSelectedFieldsFromMastersAsync([], ProductMasterSyncFields.All));
        await Assert.ThrowsAsync<ArgumentException>(() => repository.PullSelectedFieldsFromMastersAsync([Guid.NewGuid()], ProductMasterSyncFields.None));
        await Assert.ThrowsAsync<ArgumentException>(() => repository.PullSelectedFieldsFromMastersAsync([Guid.NewGuid()], (ProductMasterSyncFields)16));
        f.Session.Transaction!.Dispose();
        f.Session.Transaction = null;
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.PullSelectedFieldsFromMastersAsync([], ProductMasterSyncFields.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.PullSelectedFieldsFromMastersAsync([Guid.NewGuid()], ProductMasterSyncFields.All));
        Assert.HasCount(0, f.Connection.Commands);
    }

    /// <summary>拉取传递预取消，不执行命令或接管事务；专用供应商设置原契约没有取消参数。</summary>
    [TestMethod]
    public async Task ProductSync_PreCanceledMethodsDoNotExecuteOrOwnTransaction()
    {
        using var f = new Fixture();
        var repository = new PostgreSqlProductRepository(DictionaryContext(f));
        var token = new CancellationToken(true);
        var uid = Guid.NewGuid();
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
