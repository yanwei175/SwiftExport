using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Core.Entities.Systems.Dictionaries.Types;
using SwiftExport.Infrastructure.Repositories.Systems.Dictionaries.Types;

namespace SwiftExport.Application.Tests.Database;

/// <summary>通过真实 Dapper 和离线连接核对同步 SQL、身份及事务，不代替真实 PostgreSQL 锁验收。</summary>
public sealed partial class PostgreSqlRepositoryTests
{
    /// <summary>同步锁和候选读取共享调用方事务；包含软删除记录并按身份稳定加锁。</summary>
    [TestMethod]
    public async Task BuiltInDictionarySync_LocksBeforeReadingAndRequiresTransaction()
    {
        using var fixture = new Fixture();
        var repository = new PostgreSqlSysDictTypeRepository(DictionaryContext(fixture));
        await repository.LockBuiltInSyncAsync();
        StringAssert.Contains(fixture.Connection.Text, "pg_advisory_xact_lock");
        StringAssert.Contains(fixture.Connection.Text, "SwiftExport.BuiltInDictionarySync");
        Assert.AreSame(fixture.Session.Transaction, fixture.Connection.LastTransaction);
        await repository.GetAllIncludingDeletedAsync(true);
        StringAssert.Contains(fixture.Connection.Text, "ORDER BY uid FOR UPDATE");
        Assert.IsFalse(fixture.Connection.Text.Contains("WHERE", StringComparison.Ordinal));
        fixture.Session.Transaction!.Dispose();
        fixture.Session.Transaction = null;
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => repository.LockBuiltInSyncAsync());
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => repository.SaveBuiltInAsync(new() { DictCode = "Unit", DictName = "单位" }, 0));
        Assert.HasCount(2, fixture.Connection.Commands);
    }

    /// <summary>首次同步保留给定身份，参数化保存创建审计；主键或业务键竞争返回冲突，不覆盖既有记录。</summary>
    [TestMethod]
    public async Task BuiltInDictionarySync_InsertPreservesGivenIdentityAndReportsConflict()
    {
        using var fixture = new Fixture();
        var repository = new PostgreSqlSysDictTypeRepository(DictionaryContext(fixture));
        var entity = new SysDictType { Uid = Guid.NewGuid(), DictCode = "Unit", DictName = "单位" };
        var uid = entity.Uid;
        var result = await repository.SaveBuiltInAsync(entity, 0);
        Assert.IsTrue(result.Success);
        Assert.AreEqual(uid, fixture.Connection.Values["Uid"]);
        Assert.AreEqual(0, result.OldVersion);
        Assert.AreEqual(1, result.NewVersion);
        Assert.AreEqual("映射测试", fixture.Connection.Values["CreatedBy"]);
        Assert.AreEqual(true, fixture.Connection.Values["IsSystem"]);
        Assert.AreEqual(false, fixture.Connection.Values["IsDeleted"]);
        Assert.AreSame(fixture.Session.Transaction, fixture.Connection.LastTransaction);
        StringAssert.Contains(fixture.Connection.Text, "WHERE NOT EXISTS");
        StringAssert.Contains(fixture.Connection.Text, "lower(btrim(dict_code, ' '))");
        StringAssert.Contains(fixture.Connection.Text, "ON CONFLICT DO NOTHING");
        fixture.Connection.ExecuteWrite = () => 0;
        result = await repository.SaveBuiltInAsync(entity, 0);
        Assert.IsFalse(result.Success);
        Assert.AreEqual(ServiceErrorCodes.ConcurrencyConflict, result.ErrorCode);
    }

    /// <summary>修复类型只更新有效标记与修改审计，不覆盖编码、名称、分组、层级和创建审计；版本冲突失败。</summary>
    [TestMethod]
    public async Task BuiltInDictionarySync_RepairPreservesConfigurationAndChecksVersion()
    {
        using var fixture = new Fixture();
        fixture.Connection.Rows.Columns.Add("Version", typeof(int));
        fixture.Connection.Rows.Rows.Add(4);
        var repository = new PostgreSqlSysDictTypeRepository(DictionaryContext(fixture));
        var entity = new SysDictType { Uid = Guid.NewGuid(), Version = 3, DictCode = "Unit", DictName = "自定义名称", IsDeleted = true };
        var result = await repository.SaveBuiltInAsync(entity, 3);
        Assert.IsTrue(result.Success);
        Assert.AreEqual(3, result.OldVersion);
        Assert.AreEqual(4, result.NewVersion);
        Assert.AreEqual(3, fixture.Connection.Values["ExpectedVersion"]);
        StringAssert.Contains(fixture.Connection.Text, "WHERE uid=@Uid AND version=@ExpectedVersion RETURNING version");
        StringAssert.Contains(fixture.Connection.Text, "is_deleted=FALSE, is_system=TRUE");
        foreach (var column in new[] { "dict_code", "dict_name", "group_code", "group_name", "parent_id", "sort_order", "remark", "created_date", "created_by" })
            Assert.IsFalse(fixture.Connection.Text.Contains(column + "=", StringComparison.Ordinal), column);
        fixture.Connection.Rows.Rows.Clear();
        result = await repository.SaveBuiltInAsync(entity, 3);
        Assert.IsFalse(result.Success);
        Assert.AreEqual(ServiceErrorCodes.ConcurrencyConflict, result.ErrorCode);
    }
}
