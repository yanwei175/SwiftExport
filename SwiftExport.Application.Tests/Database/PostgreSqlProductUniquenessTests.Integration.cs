using Dapper;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using SwiftExport.Core.Entities.MasterData;

namespace SwiftExport.Application.Tests.Database;

/// <summary>型号及中性编码索引部署后的实库验收；所有随机夹具仅回滚，不提交或修改旧行。</summary>
[TestClass]
public sealed partial class PostgreSqlProductUniquenessIntegrationTests
{
    /// <summary>大小写和尾部 ASCII 空格必须发生指定编码索引的 23505，恢复保存点后继续读取。</summary>
    [TestMethod]
    [TestCategory("PostgreSqlProductUniquenessIntegration")]
    [DataRow(true, false)]
    [DataRow(true, true)]
    [DataRow(false, false)]
    [DataRow(false, true)]
    public Task EquivalentCode_InsertRejectsExactIndex(bool master, bool trailing) => InRollback(async context =>
    {
        var code = Prefix() + "Ab";
        await Insert(context, await NewEntity(context, master, code));
        var duplicate = await NewEntity(context, master, trailing ? code + " " : code.ToLowerInvariant());
        await ExpectCollision(context, CodeIndex(master), () => Insert(context, duplicate));
    });

    /// <summary>前导空格与重音有区别，软删行释放编码；中性记录用不同父和颜色隔离组合唯一键。</summary>
    [TestMethod]
    [TestCategory("PostgreSqlProductUniquenessIntegration")]
    [DataRow(true)]
    [DataRow(false)]
    public Task DistinctCodeAndSoftDelete_KeepOriginalBoundary(bool master) => InRollback(async context =>
    {
        var prefix = Prefix();
        foreach (var code in new[] { prefix + "e", " " + prefix + "e", prefix + "é" })
            await Insert(context, await NewEntity(context, master, code));
        var deleted = await NewEntity(context, master, prefix + "Deleted");
        await Insert(context, deleted);
        await context.Session.Connection.ExecuteAsync($"UPDATE public.{Table(master)} SET is_deleted=TRUE WHERE uid=@Uid",
            new { deleted.Uid }, context.Session.Transaction);
        var reused = await NewEntity(context, master, prefix.ToLowerInvariant() + "deleted ");
        await Insert(context, reused);
        Assert.AreEqual(1, await context.Session.Connection.ExecuteScalarAsync<int>(
            $"SELECT count(*) FROM public.{Table(master)} WHERE uid=@Uid AND is_deleted=FALSE", new { reused.Uid }, context.Session.Transaction));
    });

    /// <summary>空串、ASCII 空格和 U200B 的有限空键边界；已有空键仅只读识别后跳过本例，不清理旧行。</summary>
    [TestMethod]
    [TestCategory("PostgreSqlProductUniquenessIntegration")]
    [DataRow(true)]
    [DataRow(false)]
    public Task EmptyAndSpaces_UseSameActiveUniqueKey(bool master) => InRollback(async context =>
    {
        var occupied = await context.Session.Connection.ExecuteScalarAsync<bool>(
            $"SELECT EXISTS (SELECT 1 FROM public.{Table(master)} WHERE is_deleted=FALSE AND rtrim({Column(master)},' ') COLLATE public.erp_dictionary_ci = '' COLLATE public.erp_dictionary_ci)",
            transaction: context.Session.Transaction);
        if (occupied) Assert.Inconclusive("已有活动空键；本例不写入或清理旧行，竞争测试独立使用随机编码。");
        await Insert(context, await NewEntity(context, master, ""));
        var spaces = await NewEntity(context, master, "   ");
        await ExpectCollision(context, CodeIndex(master), () => Insert(context, spaces));
        var invisible = await NewEntity(context, master, "\u200B");
        await ExpectCollision(context, CodeIndex(master), () => Insert(context, invisible));
    });

    /// <summary>只验证已知 U200B 可忽略边界，不声称 ICU 完整等价 SQL Server 或所有 Unicode 字符。</summary>
    [TestMethod]
    [TestCategory("PostgreSqlProductUniquenessIntegration")]
    [DataRow(true)]
    [DataRow(false)]
    public Task U200B_LimitedComparisonAndUniqueCollision(bool master) => InRollback(async context =>
    {
        Assert.IsTrue(await context.Session.Connection.ExecuteScalarAsync<bool>(
            "SELECT CAST(@Invisible AS text) COLLATE public.erp_dictionary_ci = '' COLLATE public.erp_dictionary_ci AND CAST(@Invisible AS text) COLLATE \"C\" <> '' COLLATE \"C\"",
            new { Invisible = "\u200B" }, context.Session.Transaction));
        var code = Prefix() + "Unicode";
        await Insert(context, await NewEntity(context, master, code));
        var duplicate = await NewEntity(context, master, code + "\u200B");
        await ExpectCollision(context, CodeIndex(master), () => Insert(context, duplicate));
    });

    /// <summary>编码列仍为非空列；NULL 不得因表达式索引变更获得多空值或默认空串语义。</summary>
    [TestMethod]
    [TestCategory("PostgreSqlProductUniquenessIntegration")]
    [DataRow(true)]
    [DataRow(false)]
    public Task NullCode_RemainsNotNullViolation(bool master) => InRollback(async context =>
    {
        var entity = await NewEntity(context, master, null!);
        await Savepoint(context);
        var error = await Assert.ThrowsAsync<PostgresException>(() => Insert(context, entity));
        Assert.AreEqual("23502", error.SqlState);
        Assert.AreEqual(Column(master), error.ColumnName);
        await Restore(context);
    });

    /// <summary>碰撞更新同时改变版本、审计及业务字段；恢复保存点后用整行 JSON 验证所有字段原样保留。</summary>
    [TestMethod]
    [TestCategory("PostgreSqlProductUniquenessIntegration")]
    [DataRow(true)]
    [DataRow(false)]
    public Task CollidingUpdate_SavepointPreservesEveryStoredField(bool master) => InRollback(async context =>
    {
        var prefix = Prefix();
        await Insert(context, await NewEntity(context, master, prefix + "Taken"));
        var original = await NewEntity(context, master, prefix + "Original ");
        await Insert(context, original);
        var before = await Snapshot(context, master, original.Uid);
        var extra = master ? "default_description_zh='修改描述',default_pcs_per_package=2" : "image_path='修改图片',default_sales_price_usd=99,is_main_image=TRUE";
        await ExpectCollision(context, CodeIndex(master), () => context.Session.Connection.ExecuteAsync(
            $"UPDATE public.{Table(master)} SET {Column(master)}=@Code,version=version+1,updated_by='修改审计',updated_date=timestamp '2026-10-04 01:02:03',{extra} WHERE uid=@Uid AND version=1",
            new { Code = prefix.ToLowerInvariant() + "taken ", original.Uid }, context.Session.Transaction));
        Assert.AreEqual(before, await Snapshot(context, master, original.Uid));
    });

    /// <summary>组合唯一键仍限制有效父型号和颜色；跨父迁入已有组合也失败，不同编码不能绕过组合约束。</summary>
    [TestMethod]
    [TestCategory("PostgreSqlProductUniquenessIntegration")]
    public Task MasterColorCombination_InsertAndReparentStillReject() => InRollback(async context =>
    {
        var first = (ProductNeutral)await NewEntity(context, false, Prefix());
        await Insert(context, first);
        var second = (ProductNeutral)await NewEntity(context, false, Prefix());
        second.ColorId = first.ColorId;
        await Insert(context, second);
        var duplicate = new ProductNeutral { ProductCode = Prefix(), ProductMasterId = first.ProductMasterId, ColorId = first.ColorId };
        await ExpectCollision(context, CombinationIndex, () => Insert(context, duplicate));
        var before = await Snapshot(context, false, second.Uid);
        await ExpectCollision(context, CombinationIndex, () => context.Session.Connection.ExecuteAsync(
            "UPDATE public.product_neutral SET product_master_id=@Parent,version=version+1,image_path='迁入' WHERE uid=@Uid",
            new { Parent = first.ProductMasterId, second.Uid }, context.Session.Transaction));
        Assert.AreEqual(before, await Snapshot(context, false, second.Uid));
        await context.Session.Connection.ExecuteAsync("UPDATE public.product_neutral SET is_deleted=TRUE WHERE uid=@Uid", new { first.Uid }, context.Session.Transaction);
        await Insert(context, duplicate);
    });

    /// <summary>三个真实 PG 仓储的 Add/Update 返回精确占用码；技术失败事务先恢复保存点，再验证数据库未部分更新。</summary>
    [TestMethod]
    [TestCategory("PostgreSqlProductUniquenessIntegration")]
    [DataRow("master", false, false)]
    [DataRow("master", true, false)]
    [DataRow("neutral", false, false)]
    [DataRow("neutral", true, false)]
    [DataRow("manager", false, false)]
    [DataRow("manager", true, false)]
    [DataRow("neutral", false, true)]
    [DataRow("neutral", true, true)]
    [DataRow("manager", false, true)]
    [DataRow("manager", true, true)]
    public Task RepositoryWrite_KnownUniqueConflictReturnsBusinessCode(string repository, bool update, bool combination) => InRollback(async context =>
    {
        var master = repository == "master";
        var owner = await NewEntity(context, master, Prefix() + "Taken");
        await Insert(context, owner);
        var candidate = await NewEntity(context, master, Prefix() + "Original");
        string? before = null;
        if (update) { await Insert(context, candidate); before = await Snapshot(context, master, candidate.Uid); }
        if (master) ((ProductMaster)candidate).MasterSku = ((ProductMaster)owner).MasterSku.ToLowerInvariant() + " ";
        else if (combination)
        {
            ((ProductNeutral)candidate).ProductMasterId = ((ProductNeutral)owner).ProductMasterId;
            ((ProductNeutral)candidate).ColorId = ((ProductNeutral)owner).ColorId;
        }
        else ((ProductNeutral)candidate).ProductCode = ((ProductNeutral)owner).ProductCode.ToLowerInvariant() + " ";
        await Savepoint(context);
        var result = await Write(context, repository, candidate, update);
        Assert.IsFalse(result.Success);
        Assert.AreEqual(master ? "PRODUCT_MASTER_SKU_OCCUPIED" : combination ? "PRODUCT_NEUTRAL_MASTER_COLOR_OCCUPIED" : "PRODUCT_NEUTRAL_CODE_OCCUPIED", result.ErrorCode);
        Assert.AreEqual(0, result.AffectedRows);
        Assert.IsNull(result.NewVersion);
        Assert.AreEqual(candidate.Uid, result.Uid);
        if (update) Assert.AreEqual(1, result.OldVersion);
        else Assert.IsNull(result.OldVersion);
        await Restore(context);
        if (update) Assert.AreEqual(before, await Snapshot(context, master, candidate.Uid));
        else Assert.AreEqual(0, await context.Session.Connection.ExecuteScalarAsync<int>(
            $"SELECT count(*) FROM public.{Table(master)} WHERE uid=@Uid", new { candidate.Uid }, context.Session.Transaction));
    });
}
