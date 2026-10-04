using Microsoft.VisualStudio.TestTools.UnitTesting;
using NpgsqlTypes;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Infrastructure.Repositories.Products.ProductNeutrals;

namespace SwiftExport.Application.Tests.Database;

/// <summary>复用离线公共夹具，验证中性产品投影、参数、事务及取消，不模拟真实并发。</summary>
public sealed partial class PostgreSqlRepositoryTests
{
    /// <summary>明细还原可空价格和颜色名，外连接锁只作用于 pn。</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Neutral_DetailMapsDtoWithOptionalNeutralLock(bool locking)
    {
        using var f = new Fixture();
        var uid = Guid.NewGuid();
        var master = Guid.NewGuid();
        var color = Guid.NewGuid();
        var rows = f.Connection.Rows;
        rows.Columns.Add("Uid", typeof(Guid)); rows.Columns.Add("Version", typeof(int));
        rows.Columns.Add("ProductMasterId", typeof(Guid)); rows.Columns.Add("ColorId", typeof(Guid));
        rows.Columns.Add("ProductCode", typeof(string)); rows.Columns.Add("ColorName", typeof(string));
        rows.Columns.Add("DefaultSalesPriceUsd", typeof(decimal)); rows.Columns.Add("IsMainImage", typeof(bool));
        rows.Rows.Add(uid, 3, master, color, "Ab ", DBNull.Value, DBNull.Value, true);
        var dto = (await new PostgreSqlProductNeutralRepository(DictionaryContext(f)).GetByProductMasterIdAsync(master, locking)).Single();
        Assert.AreEqual(uid, dto.Uid); Assert.AreEqual(3, dto.Version); Assert.AreEqual(master, dto.ProductMasterId);
        Assert.AreEqual(color, dto.Data.ColorId); Assert.AreEqual("Ab ", dto.Data.ProductCode);
        Assert.IsTrue(dto.Data.IsMainImage); Assert.IsNull(dto.Data.DefaultSalesPriceUsd); Assert.IsNull(dto.ColorName);
        Assert.AreEqual(locking, f.Connection.Text.Contains("FOR UPDATE OF pn", StringComparison.Ordinal));
        StringAssert.Contains(f.Connection.Text, "rtrim(pc.color_name_zh, ' ')");
        StringAssert.Contains(f.Connection.Text, "pc.is_deleted=FALSE");
        StringAssert.Contains(f.Connection.Text, "ORDER BY pn.product_code COLLATE public.erp_dictionary_ci, pn.uid");
        Assert.AreSame(f.Session.Transaction, f.Connection.LastTransaction);
    }

    /// <summary>编码规范化输入但保留原码返回，text 数组和显式锁按原边界执行。</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Neutral_CodesUseArrayAndOptionalLock(bool locking)
    {
        using var f = new Fixture();
        f.Connection.Rows.Columns.Add("ProductCode", typeof(string)); f.Connection.Rows.Rows.Add("Ab ");
        var result = await new PostgreSqlProductNeutralRepository(DictionaryContext(f)).GetByProductCodesAsync([" Ab ", "AB", " "], locking);
        Assert.AreEqual("Ab ", result.Single().ProductCode);
        CollectionAssert.AreEqual(new[] { "Ab" }, (string[])f.Connection.Values["Codes"]!);
        Assert.AreEqual(NpgsqlDbType.Array | NpgsqlDbType.Text, f.Connection.Types["Codes"]);
        StringAssert.Contains(f.Connection.Text, "EXISTS (SELECT 1 FROM unnest(@Codes)");
        Assert.AreEqual(locking, f.Connection.Text.Contains("FOR UPDATE", StringComparison.Ordinal));
        Assert.AreSame(f.Session.Transaction, f.Connection.LastTransaction);
    }

    /// <summary>空编码不查询，非空锁读必须有事务，三个取消入口均不执行 SQL。</summary>
    [TestMethod]
    public async Task Neutral_EmptyLockAndCancellationDoNotExecute()
    {
        using var f = new Fixture();
        f.Session.Transaction!.Dispose(); f.Session.Transaction = null;
        var repository = new PostgreSqlProductNeutralRepository(DictionaryContext(f));
        Assert.HasCount(0, await repository.GetByProductCodesAsync([], true));
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.GetByProductCodesAsync(["A"], true));
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.GetByProductMasterIdAsync(Guid.NewGuid(), true));
        var token = new CancellationToken(true);
        await Assert.ThrowsAsync<OperationCanceledException>(() => repository.GetByProductMasterIdAsync(Guid.NewGuid(), cancellationToken: token));
        await Assert.ThrowsAsync<OperationCanceledException>(() => repository.GetByProductCodesAsync(["A"], cancellationToken: token));
        await Assert.ThrowsAsync<OperationCanceledException>(() => repository.SoftDeleteByProductMasterIdAsync(Guid.NewGuid(), token));
        Assert.HasCount(0, f.Connection.Commands);
        await repository.GetByProductMasterIdAsync(Guid.NewGuid());
        Assert.IsNull(f.Connection.LastTransaction);
        Assert.IsFalse(f.Connection.Text.Contains("FOR UPDATE", StringComparison.Ordinal));
    }

    /// <summary>级联零行成功，审计使用无时区本地时间，共享事务且不提交。</summary>
    [TestMethod]
    public async Task Neutral_CascadeZeroRowsPreservesSuccessAndAudit()
    {
        using var f = new Fixture(); f.Connection.ExecuteWrite = () => 0;
        var id = Guid.NewGuid();
        var result = await new PostgreSqlProductNeutralRepository(DictionaryContext(f)).SoftDeleteByProductMasterIdAsync(id);
        Assert.IsTrue(result.Success); Assert.AreEqual(id, result.Uid); Assert.IsNull(result.NewVersion);
        Assert.AreEqual(DateTimeKind.Unspecified, ((DateTime)f.Connection.Values["UpdatedDate"]!).Kind);
        Assert.AreEqual("映射测试", f.Connection.Values["UpdatedBy"]);
        StringAssert.Contains(f.Connection.Text, "version=version+1");
        StringAssert.Contains(f.Connection.Text, "product_master_id=@ProductMasterId AND is_deleted=FALSE");
        Assert.AreSame(f.Session.Transaction, f.Connection.LastTransaction);
        Assert.AreEqual(0, f.Connection.Commits); Assert.AreEqual(0, f.Connection.Rollbacks);
    }

    /// <summary>继承新增更新保留版本条件、可空价格及公共参数映射。</summary>
    [TestMethod]
    public async Task Neutral_CrudPreservesVersionAndNullablePrices()
    {
        using var f = new Fixture();
        var repository = new PostgreSqlProductNeutralRepository(DictionaryContext(f));
        var entity = new ProductNeutral { ProductCode = "A", ProductMasterId = Guid.NewGuid(), ColorId = Guid.NewGuid() };
        Assert.AreEqual(1, (await repository.AddAsync(entity)).NewVersion);
        Assert.AreEqual(DBNull.Value, f.Connection.Values["DefaultExchangeRate"]);
        f.Connection.Rows.Columns.Add("Version", typeof(int)); f.Connection.Rows.Rows.Add(2);
        Assert.AreEqual(2, (await repository.UpdateAsync(entity)).NewVersion);
        StringAssert.Contains(f.Connection.Text, "\"version\" = @OriginalVersion");
        Assert.AreSame(f.Session.Transaction, f.Connection.LastTransaction);
    }
}
