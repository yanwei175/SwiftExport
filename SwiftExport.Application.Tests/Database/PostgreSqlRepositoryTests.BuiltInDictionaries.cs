using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Core.Entities.Systems.Dictionaries.Data;
using SwiftExport.Infrastructure.Repositories.Systems.Dictionaries.Data;

namespace SwiftExport.Application.Tests.Database;

/// <summary>复用离线 Dapper 夹具验证内置覆盖的 SQL、身份和回执；不代替真实 PostgreSQL 并发验收。</summary>
public sealed partial class PostgreSqlRepositoryTests
{
    /// <summary>维护只锁定请求身份且包含软删除事实；明细 SQL 明确按 Uid 排序取锁，不依赖请求顺序或类型是否落库。</summary>
    [TestMethod]
    public async Task BuiltInDictionaryMaintenanceLocksDeletedTargetsInStableOrder()
    {
        using var fixture = new Fixture();
        fixture.Connection.Rows.Columns.Add("Uid", typeof(Guid));
        fixture.Connection.Rows.Columns.Add("IsDeleted", typeof(bool));
        var deletedId = Guid.NewGuid();
        var activeId = Guid.NewGuid();
        fixture.Connection.Rows.Rows.Add(deletedId, true);
        fixture.Connection.Rows.Rows.Add(activeId, false);
        var repository = new PostgreSqlSysDictDataRepository(DictionaryContext(fixture));
        var rows = await repository.GetByIdsForUpdateAsync([deletedId, activeId, deletedId]);
        Assert.HasCount(2, rows);
        Assert.IsTrue(rows.Single(item => item.Uid == deletedId).IsDeleted);
        CollectionAssert.AreEquivalent(new[] { deletedId, activeId }, (Guid[])fixture.Connection.Values["Uids"]!);
        StringAssert.Contains(fixture.Connection.Text, "WHERE uid = ANY(@Uids) ORDER BY uid FOR UPDATE");
        Assert.IsFalse(fixture.Connection.Text.Contains("is_deleted = FALSE", StringComparison.Ordinal));
        Assert.AreSame(fixture.Session.Transaction, fixture.Connection.LastTransaction);
        await repository.GetByIdsForUpdateAsync([]);
        Assert.HasCount(1, fixture.Connection.Commands);
        fixture.Session.Transaction!.Dispose();
        fixture.Session.Transaction = null;
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => repository.GetByIdsForUpdateAsync([deletedId]));
        Assert.HasCount(1, fixture.Connection.Commands);
    }

    /// <summary>合并读取包含软删除事实且不写入；更新锁要求外层事务，读取按身份稳定排序。</summary>
    [TestMethod]
    public async Task BuiltInDictionaryReadKeepsDeletedFactsAndBorrowedLocks()
    {
        using var fixture = new Fixture();
        fixture.Connection.Rows.Columns.Add("Uid", typeof(Guid));
        fixture.Connection.Rows.Columns.Add("IsDeleted", typeof(bool));
        var id = Guid.NewGuid();
        fixture.Connection.Rows.Rows.Add(id, true);
        var repository = new PostgreSqlSysDictDataRepository(DictionaryContext(fixture));
        var rows = await repository.GetForBuiltInMergeAsync(" FlowTestFunction ", true);
        Assert.AreEqual(id, rows.Single().Uid);
        Assert.IsTrue(rows.Single().IsDeleted);
        Assert.AreEqual("FlowTestFunction", fixture.Connection.Values["DictCode"]);
        StringAssert.Contains(fixture.Connection.Text, "btrim(dict_code, ' ') = @DictCode");
        StringAssert.Contains(fixture.Connection.Text, "ORDER BY uid FOR UPDATE");
        Assert.IsFalse(fixture.Connection.Text.Contains("is_deleted = FALSE", StringComparison.Ordinal));
        Assert.AreSame(fixture.Session.Transaction, fixture.Connection.LastTransaction);
        fixture.Session.Transaction!.Dispose();
        fixture.Session.Transaction = null;
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => repository.GetForBuiltInMergeAsync(null, true));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => repository.SaveBuiltInOverrideAsync(new(), 0));
        Assert.HasCount(1, fixture.Connection.Commands);
    }

    /// <summary>零版本首次保存保留给定 Guid、返回 0→1，并用数据库主键处理竞争，不继续执行隐式覆盖。</summary>
    [TestMethod]
    public async Task BuiltInDictionaryFirstSaveKeepsIdentityAndReportsConflict()
    {
        using var fixture = new Fixture();
        var repository = new PostgreSqlSysDictDataRepository(DictionaryContext(fixture));
        var id = Guid.Parse("979dc2d0-eeba-40a2-bfa0-1ea1c934aa4c");
        var entity = new SysDictData { Uid = id, DictCode = "FlowTestFunction", DataNameEn = "Default", DataNameZh = "默认功能" };
        var result = await repository.SaveBuiltInOverrideAsync(entity, 0);
        Assert.IsTrue(result.Success);
        Assert.AreEqual(id, entity.Uid);
        Assert.AreEqual(id, fixture.Connection.Values["Uid"]);
        Assert.AreEqual(0, result.OldVersion);
        Assert.AreEqual(1, result.NewVersion);
        Assert.AreEqual(1, entity.Version);
        Assert.AreEqual("映射测试", fixture.Connection.Values["CreatedBy"]);
        Assert.AreSame(fixture.Session.Transaction, fixture.Connection.LastTransaction);
        StringAssert.Contains(fixture.Connection.Text, "ON CONFLICT DO NOTHING");
        StringAssert.Contains(fixture.Connection.Text, "WHERE NOT EXISTS");
        StringAssert.Contains(fixture.Connection.Text, "btrim(data_name_en, ' ') = btrim(@DataNameEn, ' ')");
        fixture.Connection.ExecuteWrite = () => 0;
        result = await repository.SaveBuiltInOverrideAsync(entity, 0);
        Assert.IsFalse(result.Success);
        Assert.AreEqual(ServiceErrorCodes.ConcurrencyConflict, result.ErrorCode);
        Assert.AreEqual(0, result.OldVersion);
        Assert.IsNull(result.NewVersion);
    }

    /// <summary>更新只覆盖显示字段和有效标记，保留身份、业务键及创建审计；版本冲突不产生成功回执。</summary>
    [TestMethod]
    public async Task BuiltInDictionaryUpdatePreservesCreationAndBusinessKey()
    {
        using var fixture = new Fixture();
        fixture.Connection.Rows.Columns.Add("Version", typeof(int));
        fixture.Connection.Rows.Rows.Add(4);
        var repository = new PostgreSqlSysDictDataRepository(DictionaryContext(fixture));
        var created = new DateTime(2026, 1, 1);
        var entity = new SysDictData
        {
            Uid = Guid.NewGuid(), Version = 3, DictCode = "FlowLimiterSpecification", DataNameEn = "Unlimited",
            DataNameZh = "不限流量", CreatedDate = created, CreatedBy = "旧创建者", IsDeleted = true
        };
        var result = await repository.SaveBuiltInOverrideAsync(entity, 3);
        Assert.IsTrue(result.Success);
        Assert.AreEqual(4, entity.Version);
        Assert.AreEqual(4, result.OldVersion);
        Assert.AreEqual(4, result.NewVersion);
        Assert.AreEqual(created, entity.CreatedDate);
        Assert.AreEqual("旧创建者", entity.CreatedBy);
        Assert.AreEqual(3, fixture.Connection.Values["ExpectedVersion"]);
        StringAssert.Contains(fixture.Connection.Text, "WHERE uid=@Uid AND version=@ExpectedVersion RETURNING version");
        StringAssert.Contains(fixture.Connection.Text, "is_deleted=FALSE, is_system=TRUE, is_enabled=TRUE");
        Assert.IsFalse(fixture.Connection.Text.Contains("created_date=", StringComparison.Ordinal));
        Assert.IsFalse(fixture.Connection.Text.Contains("dict_code=", StringComparison.Ordinal));
        Assert.IsFalse(fixture.Connection.Text.Contains("data_name_en=", StringComparison.Ordinal));
        fixture.Connection.Rows.Rows.Clear();
        result = await repository.SaveBuiltInOverrideAsync(entity, 3);
        Assert.IsFalse(result.Success);
        Assert.AreEqual(ServiceErrorCodes.ConcurrencyConflict, result.ErrorCode);
    }
}
