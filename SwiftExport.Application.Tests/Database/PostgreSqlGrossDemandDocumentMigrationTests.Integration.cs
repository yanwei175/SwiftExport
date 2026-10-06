using System.Buffers.Binary;
using System.Data;
using System.Data.SqlTypes;
using System.Security.Cryptography;
using System.Text;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using SwiftExport.Application.Commands;
using SwiftExport.Application.Contexts;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.DemandPlanning;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;
using SwiftExport.Infrastructure.Repositories.Commands;
using SwiftExport.Infrastructure.Repositories.DemandPlanning.Sources.Document;
using SwiftExport.Infrastructure.Services.Auth;

namespace SwiftExport.Application.Tests.Database;

/// <summary>来源九方法真实PG验收；只在严格故障库门禁下运行，提交夹具仅精确删除本例随机Gross UID，不修改约束或回执。</summary>
[TestClass, DoNotParallelize, TestCategory("PostgreSqlIntegration")]
public sealed class PostgreSqlGrossDemandDocumentMigrationIntegrationTests
{
    private static readonly DateTime Wall = new(2026, 10, 5, 8, 9, 10, DateTimeKind.Unspecified);
    private static readonly EntityMetadata Map = PostgreSqlEntityMetadata.GetMetadata(typeof(GrossDemand));
    private static readonly string[] SourceFields = ["SourceNo", "SourceLineNumber", "SourceVersion", "DemandBasisFingerprint", "CustomerId", "ProductId", "IsNonStandard", "SkuSnapshot", "LinkSkuSnapshot", "CustomerSkuSnapshot", "DescriptionSnapshot", "ColorSnapshot", "SpecificationSnapshot", "ImagePathSnapshot", "UnitSnapshot", "DemandQty", "RequiredDate", "Remark"];
    private static readonly string[] ManualFields = ["ProductId", "IsNonStandard", "SkuSnapshot", "LinkSkuSnapshot", "CustomerSkuSnapshot", "DescriptionSnapshot", "ColorSnapshot", "SpecificationSnapshot", "ImagePathSnapshot", "DemandBasisFingerprint", "DemandQty", "RequiredDate", "Remark"];

    /// <summary>新增保留给定身份、正版本及已给审计，只补NULL；数据库未删常量与输入实体删除标志分别核对。</summary>
    [TestMethod]
    [DataRow(0)] [DataRow(-7)]
    public async Task Add_PreservesEnumerationIdentityVersionAndFullProjection(int nonpositiveVersion)
    {
        await using var f = new Fixture();
        var first = Demand(); first.Version = 7; first.IsDeleted = true; first.CreatedBy = ""; first.UpdatedBy = "原更新人";
        var second = Demand(first.SourceUid); second.Uid = Guid.Empty; second.Version = nonpositiveVersion;
        second.CreatedDate = null; second.UpdatedDate = null; second.CreatedBy = null; second.UpdatedBy = null;
        await f.PrepareBegin([first.SourceUid], [first.Uid]);
        var before = DateTime.Now;
        var results = await f.Repository.AddSourceDemandsAsync([first, second]);
        var after = DateTime.Now;
        Assert.HasCount(2, results);
        AssertWrite(results[0], first.Uid, null, 7, "新增成功。");
        Assert.AreNotEqual(Guid.Empty, second.Uid); Assert.AreEqual(1, second.Version);
        AssertWrite(results[1], second.Uid, null, 1, "新增成功。");
        Assert.IsTrue(first.IsDeleted, "INSERT常量不能回填或重置输入实体。");
        var expected = Clone(first); expected.IsDeleted = false;
        AssertEntity(expected, (await f.Read(first.Uid))!);
        Assert.AreEqual("", first.CreatedBy); Assert.AreEqual("原更新人", first.UpdatedBy);
        Assert.AreEqual(TestUser.Name, second.CreatedBy); Assert.AreEqual(TestUser.Name, second.UpdatedBy);
        Assert.IsTrue(second.CreatedDate >= before && second.CreatedDate <= after);
        Assert.AreEqual(second.CreatedDate, second.UpdatedDate);
        AssertEntity(second, (await f.Read(second.Uid))!);
    }

    /// <summary>第二行约束失败不被包装成成功；父事务可回滚已经插入的首行，输入打戳边界仍遵循逐行枚举。</summary>
    [TestMethod]
    [DataRow("duplicate-uid")]
    [DataRow("invalid-quantity")]
    public async Task Add_LaterSqlFailureRollsBackPriorRow(string failure)
    {
        await using var f = new Fixture(); var first = Demand(); var second = Demand(first.SourceUid);
        if (failure == "duplicate-uid") second.Uid = first.Uid; else second.DemandQty = 0;
        await f.PrepareBegin([first.SourceUid], [first.Uid, second.Uid]);
        var error = await Assert.ThrowsExactlyAsync<PostgresException>(() => f.Repository.AddSourceDemandsAsync([first, second]));
        Assert.AreEqual(failure == "duplicate-uid" ? PostgresErrorCodes.UniqueViolation : PostgresErrorCodes.CheckViolation, error.SqlState);
        Assert.IsNotNull(f.Session.Transaction); f.End(false);
        Assert.IsNull(await f.Read(first.Uid)); Assert.IsNull(await f.Read(second.Uid));
        Assert.AreEqual(1, await f.Session.Connection.ExecuteScalarAsync<int>("SELECT 1"));
    }

    /// <summary>无父事务原本不保证批次原子性；第二行失败后首行已经提交，不偷偷增加仓储事务或吞掉数据库异常。</summary>
    [TestMethod]
    [DataRow("duplicate-uid")] [DataRow("invalid-quantity")]
    public async Task Add_NoParentLaterFailurePreservesPriorAutocommit(string failure)
    {
        await using var f = new Fixture(); await using var observer = new Fixture(); var first = Demand(); var second = Demand(first.SourceUid);
        if (failure == "duplicate-uid") second.Uid = first.Uid; else second.DemandQty = 0;
        f.Track(first.Uid); f.Track(second.Uid);
        var error = await Assert.ThrowsExactlyAsync<PostgresException>(() => f.Repository.AddSourceDemandsAsync([first, second]));
        Assert.AreEqual(failure == "duplicate-uid" ? PostgresErrorCodes.UniqueViolation : PostgresErrorCodes.CheckViolation, error.SqlState);
        Assert.IsNull(f.Session.Transaction); AssertEntity(first, (await observer.Read(first.Uid))!);
        if (failure != "duplicate-uid") Assert.IsNull(await observer.Read(second.Uid));
        Assert.AreEqual(0L, await LockCount(observer, f)); Assert.IsTrue(await TryKey(observer, "Source", first.SourceUid));
        Assert.AreEqual(1, await f.Session.Connection.ExecuteScalarAsync<int>("SELECT 1"));
    }

    /// <summary>读单UID完整映射全部字段，无生命周期过滤；软删及不存在仍返回NULL并保留父事务。</summary>
    [TestMethod]
    [DataRow(0, false)] [DataRow(8, false)] [DataRow(9, false)] [DataRow(0, true)]
    public async Task GetById_ProjectionAndDeletionBoundary(int lifecycle, bool deleted)
    {
        await using var f = new Fixture(); var row = Demand(); row.LifecycleStatus = (GrossDemandLifecycleStatusEnum)lifecycle; row.IsDeleted = deleted;
        var missing = Guid.NewGuid(); await f.PrepareBegin([row.SourceUid], [row.Uid, missing, Guid.Empty]); await f.Insert(row);
        var tx = f.Session.Transaction; var result = await f.Repository.GetByIdForUpdateAsync(row.Uid);
        if (deleted) Assert.IsNull(result); else { Assert.IsNotNull(result); AssertEntity(row, result); }
        Assert.IsNull(await f.Repository.GetByIdForUpdateAsync(missing)); Assert.IsNull(await f.Repository.GetByIdForUpdateAsync(Guid.Empty));
        Assert.AreSame(tx, f.Session.Transaction);
    }

    /// <summary>来源原始参数不Trim/Upper，NULL等号不匹配；NULL行号优先，尾UID使用SqlGuid而非.NET Guid顺序。</summary>
    [TestMethod]
    [DataRow("SO", 4)] [DataRow("so ", 4)] [DataRow(" SO", 0)] [DataRow("SO\t", 0)] [DataRow(null, 0)] [DataRow("CR", 1)]
    public async Task GetActive_ActualSourcePredicateNullAndSqlGuidOrder(string? input, int count)
    {
        await using var f = new Fixture(); var source = Guid.NewGuid();
        // 两个固定尾键只在随机来源下使用并将高位随机化，保留可证明不同的.NET/SqlGuid顺序。
        var a = Demand(source); var b = Demand(source); var bytes = Guid.NewGuid().ToByteArray(); bytes[0] = 1; bytes[10] = 2; a.Uid = new(bytes);
        bytes = (byte[])bytes.Clone(); bytes[0] = 2; bytes[10] = 1; b.Uid = new(bytes);
        a.SourceLineNumber = b.SourceLineNumber = 1;
        var n = Demand(source); n.SourceLineNumber = null; var later = Demand(source); later.SourceLineNumber = 2;
        var cr = Demand(source, "CR"); var closed = Demand(source); closed.LifecycleStatus = GrossDemandLifecycleStatusEnum.Closed;
        var voided = Demand(source); voided.LifecycleStatus = GrossDemandLifecycleStatusEnum.Voided;
        var deleted = Demand(source); deleted.IsDeleted = true; var other = Demand();
        var all = new[] { a, b, n, later, cr, closed, voided, deleted, other }; var missingSource = Guid.NewGuid();
        await f.PrepareBegin(all.Select(x => x.SourceUid).Append(missingSource).ToArray(), all.Select(x => x.Uid).ToArray());
        foreach (var row in all) await f.Insert(row);
        var result = await f.Repository.GetActiveBySourceForUpdateAsync(input!, source);
        Assert.HasCount(count, result);
        if (count == 4)
        {
            var expected = new[] { a, b, n, later }.OrderBy(x => x.SourceLineNumber).ThenBy(x => new SqlGuid(x.Uid)).ToArray();
            CollectionAssert.AreEqual(expected.Select(x => x.Uid).ToArray(), result.Select(x => x.Uid).ToArray());
            Assert.AreNotEqual(a.Uid.CompareTo(b.Uid), new SqlGuid(a.Uid).CompareTo(new SqlGuid(b.Uid)));
            for (var i = 0; i < result.Count; i++) AssertEntity(expected[i], result[i]);
        }
        if (count == 1) AssertEntity(cr, result[0]);
        Assert.HasCount(0, await f.Repository.GetActiveBySourceForUpdateAsync(input!, missingSource));
    }

    /// <summary>两更新精确核对全部映射字段的写列和排除列；版本仅输出，不回填传入实体，NULL快照也真实落库。</summary>
    [TestMethod]
    [DataRow(false, false)] [DataRow(false, true)] [DataRow(true, false)] [DataRow(true, true)]
    public async Task Update_ExactWriteColumnsAndUnchangedInput(bool manual, bool clearNullable)
    {
        await using var f = new Fixture(); var original = Demand(type: manual ? "md " : "CR"); original.Version = 4;
        await f.PrepareBegin([original.SourceUid], [original.Uid]); await f.Insert(original);
        var changed = Demand(); changed.Uid = original.Uid; changed.Version = 91; changed.SourceLineNumber = 31; changed.SourceVersion = 11;
        changed.SourceNo = "新源号"; changed.CustomerId = Guid.NewGuid(); changed.DemandQty = 19.1250m; changed.RequiredDate = Wall.AddDays(2);
        changed.SkuSnapshot = "新SKU"; changed.LinkSkuSnapshot = "新关联"; changed.CustomerSkuSnapshot = "新客户型号"; changed.DescriptionSnapshot = "新说明";
        changed.ColorSnapshot = "新颜色"; changed.SpecificationSnapshot = "新规格"; changed.ImagePathSnapshot = "新图片"; changed.UnitSnapshot = " 新单位 ";
        changed.DemandBasisFingerprint = "新指纹"; changed.Remark = "新备注"; changed.CreatedBy = "不可覆盖创建人"; changed.UpdatedBy = "不可使用客户端审计";
        changed.LifecycleStatus = GrossDemandLifecycleStatusEnum.Voided; changed.IsDeleted = true;
        changed.LifecycleChangedAt = Wall.AddYears(1); changed.LifecycleChangedBy = "不可覆盖状态人"; changed.LifecycleChangedReason = "不可覆盖状态原因";
        if (clearNullable)
        {
            changed.SourceLineNumber = null; changed.SourceVersion = null; changed.CustomerId = null; changed.DemandBasisFingerprint = null;
            changed.LinkSkuSnapshot = changed.CustomerSkuSnapshot = changed.DescriptionSnapshot = changed.ColorSnapshot = changed.SpecificationSnapshot = null;
            changed.ImagePathSnapshot = changed.UnitSnapshot = changed.Remark = null; changed.RequiredDate = null;
        }
        var beforeInput = Clone(changed); var beforeTime = DateTime.Now;
        var result = manual ? await f.Repository.UpdateManualAsync(changed, 4) : await f.Repository.UpdateSourceDemandAsync(changed, 4);
        AssertWrite(result, original.Uid, 4, 5, manual ? "手工毛需求更新成功。" : "来源需求已更新。");
        AssertEntity(beforeInput, changed);
        var stored = (await f.Read(original.Uid))!; var expected = Clone(original);
        foreach (var name in manual ? ManualFields : SourceFields) CopyProperty(changed, expected, name);
        expected.Version = 5; expected.UpdatedBy = TestUser.Name; expected.UpdatedDate = stored.UpdatedDate;
        Assert.IsTrue(stored.UpdatedDate >= FloorMicrosecond(beforeTime) && stored.UpdatedDate <= DateTime.Now);
        AssertEntity(expected, stored);
    }

    /// <summary>合法非标基线后故意传入两种不一致产品组合，真实CHECK证明ProductId和IsNonStandard确实参与两更新SET；不创建虚假产品。</summary>
    [TestMethod]
    [DataRow(false, false)] [DataRow(false, true)] [DataRow(true, false)] [DataRow(true, true)]
    public async Task Update_ProductColumnsReachDatabaseConstraint(bool manual, bool nonstandardWithProduct)
    {
        await using var f = new Fixture(); var row = Demand(type: "MD"); await f.InsertCommitted(row); await f.PrepareBegin([row.SourceUid], [row.Uid]);
        var changed = Clone(row); changed.IsNonStandard = nonstandardWithProduct; changed.ProductId = nonstandardWithProduct ? Guid.NewGuid() : null;
        var error = await Assert.ThrowsExactlyAsync<PostgresException>(() => manual
            ? f.Repository.UpdateManualAsync(changed, 1) : f.Repository.UpdateSourceDemandAsync(changed, 1));
        Assert.AreEqual(PostgresErrorCodes.CheckViolation, error.SqlState); f.End(false);
        AssertEntity(row, (await f.Read(row.Uid))!);
    }

    /// <summary>三个版本命令保留活动/未删/旧版本以及手工MD谓词，零行返回原冲突消息且无字段改变。</summary>
    [TestMethod]
    [DataRow("source", "closed")] [DataRow("source", "voided")] [DataRow("source", "deleted")] [DataRow("source", "version")] [DataRow("source", "missing")]
    [DataRow("manual", "closed")] [DataRow("manual", "deleted")] [DataRow("manual", "version")] [DataRow("manual", "type")] [DataRow("manual", "missing")]
    [DataRow("close", "closed")] [DataRow("close", "voided")] [DataRow("close", "deleted")] [DataRow("close", "version")] [DataRow("close", "type")] [DataRow("close", "missing")]
    public async Task VersionWrites_ZeroRowsKeepFullFactsAndOriginalConflict(string operation, string boundary)
    {
        await using var f = new Fixture(); var row = Demand(type: boundary == "type" ? "SO" : "MD");
        if (boundary == "closed") row.LifecycleStatus = GrossDemandLifecycleStatusEnum.Closed;
        if (boundary == "voided") row.LifecycleStatus = GrossDemandLifecycleStatusEnum.Voided;
        if (boundary == "deleted") row.IsDeleted = true;
        await f.PrepareBegin([row.SourceUid], [row.Uid]); if (boundary != "missing") await f.Insert(row);
        var changed = Clone(row); changed.Remark = "不应写入"; var version = boundary == "version" ? 2 : 1;
        var result = operation switch
        {
            "source" => await f.Repository.UpdateSourceDemandAsync(changed, version),
            "manual" => await f.Repository.UpdateManualAsync(changed, version),
            _ => await f.Repository.CloseManualAsync(row.Uid, version, "不应关闭")
        };
        AssertConflict(result, row.Uid, version, operation == "source" ? "来源需求状态或版本已变化。" : "手工毛需求状态或版本已变化，请刷新后重试。");
        if (boundary == "missing") Assert.IsNull(await f.Read(row.Uid)); else AssertEntity(row, (await f.Read(row.Uid))!);
        Assert.IsNotNull(f.Session.Transaction);
    }

    /// <summary>关闭只写生命周期、一次时间审计和Version，原因原文包括空白或NULL，不更改来源和内容。</summary>
    [TestMethod]
    [DataRow(" 原因 \t")] [DataRow("")] [DataRow(null)]
    public async Task Close_ExactLifecycleColumnsAndRawReason(string? reason)
    {
        await using var f = new Fixture(); var row = Demand(type: "mD "); await f.PrepareBegin([row.SourceUid], [row.Uid]); await f.Insert(row);
        var result = await f.Repository.CloseManualAsync(row.Uid, 1, reason!);
        AssertWrite(result, row.Uid, 1, 2, "手工毛需求已关闭。");
        var stored = (await f.Read(row.Uid))!; AssertLifecycle(row, stored, GrossDemandLifecycleStatusEnum.Closed, reason);
    }

    /// <summary>物理删除只过滤活动未删并Distinct，不增加版本条件；关闭、作废、软删、未提交身份完全保持。</summary>
    [TestMethod]
    public async Task HardDelete_PhysicalDistinctAndActiveBoundary()
    {
        await using var f = new Fixture(); var active = Demand(); active.Version = 31; var second = Demand(active.SourceUid, "CR");
        var closed = Demand(active.SourceUid); closed.LifecycleStatus = GrossDemandLifecycleStatusEnum.Closed;
        var voided = Demand(active.SourceUid); voided.LifecycleStatus = GrossDemandLifecycleStatusEnum.Voided;
        var deleted = Demand(active.SourceUid); deleted.IsDeleted = true; var untouched = Demand(active.SourceUid);
        var rows = new[] { active, second, closed, voided, deleted, untouched }; var missing = Guid.NewGuid();
        await f.PrepareBegin([active.SourceUid], rows.Select(x => x.Uid).Append(missing).ToArray()); foreach (var row in rows) await f.Insert(row);
        Assert.AreEqual(2, await f.Repository.HardDeleteForSourceCancellationAsync([active.Uid, second.Uid, active.Uid, closed.Uid, voided.Uid, deleted.Uid, missing]));
        Assert.IsNull(await f.Read(active.Uid)); Assert.IsNull(await f.Read(second.Uid));
        foreach (var row in new[] { closed, voided, deleted, untouched }) AssertEntity(row, (await f.Read(row.Uid))!);
    }

    /// <summary>两作废方法真实核对完整范围、Distinct、源类型/源UID过滤和全部生命周期审计，未涉及行不变。</summary>
    [TestMethod]
    [DataRow(false, " 原因 \t")] [DataRow(false, null)] [DataRow(true, " 原因 \t")] [DataRow(true, null)]
    public async Task Void_ExactSetAuditAndUnchangedFacts(bool bySource, string? reason)
    {
        await using var f = new Fixture(); var active = Demand(); active.Version = 7; var second = Demand(active.SourceUid); second.SourceType = "so ";
        var cr = Demand(active.SourceUid, "CR"); var other = Demand(); var closed = Demand(active.SourceUid); closed.LifecycleStatus = GrossDemandLifecycleStatusEnum.Closed;
        var voided = Demand(active.SourceUid); voided.LifecycleStatus = GrossDemandLifecycleStatusEnum.Voided;
        var deleted = Demand(active.SourceUid); deleted.IsDeleted = true; var rows = new[] { active, second, cr, other, closed, voided, deleted };
        var missing = Guid.NewGuid(); await f.PrepareBegin(rows.Select(x => x.SourceUid).ToArray(), rows.Select(x => x.Uid).Append(missing).ToArray());
        foreach (var row in rows) await f.Insert(row);
        var count = bySource ? await f.Repository.VoidPendingBySourceAsync("sO ", active.SourceUid, reason!)
            : await f.Repository.VoidRolledBackForSourceCancellationAsync([active.Uid, active.Uid, second.Uid, closed.Uid, voided.Uid, deleted.Uid, missing], reason!);
        Assert.AreEqual(2, count); var firstStored = (await f.Read(active.Uid))!; var secondStored = (await f.Read(second.Uid))!;
        AssertLifecycle(active, firstStored, GrossDemandLifecycleStatusEnum.Voided, reason);
        AssertLifecycle(second, secondStored, GrossDemandLifecycleStatusEnum.Voided, reason);
        Assert.AreEqual(firstStored.UpdatedDate, secondStored.UpdatedDate, "同一次集合命令共享同一时间值。");
        foreach (var row in new[] { cr, other, closed, voided, deleted }) AssertEntity(row, (await f.Read(row.Uid))!);
    }

    /// <summary>NULL、前导空格和非ASCII尾空白原样传SQL，不被仓储标准化后作废匹配来源。</summary>
    [TestMethod]
    [DataRow(null)] [DataRow(" SO")] [DataRow("SO\t")]
    public async Task VoidPending_UnnormalizedNonmatchPreservesRows(string? type)
    {
        await using var f = new Fixture(); var row = Demand(); await f.PrepareBegin([row.SourceUid], [row.Uid]); await f.Insert(row);
        Assert.AreEqual(0, await f.Repository.VoidPendingBySourceAsync(type!, row.SourceUid, "无匹配")); AssertEntity(row, (await f.Read(row.Uid))!);
    }

    /// <summary>空Add/硬删/作废集合在无租约或父事务下均原样早返，实际backend查询不变而非只检查返回值。</summary>
    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task EmptyEnumerations_DoNotPrepareOrIssueSql(bool parent)
    {
        await using var f = new Fixture(); await using var observer = new Fixture();
        if (parent) f.Begin(IsolationLevel.ReadCommitted);
        await f.Session.Connection.ExecuteScalarAsync<int>(new CommandDefinition("SELECT 987654", transaction: f.Session.Transaction));
        var query = await LastQuery(observer, f); var tx = f.Session.Transaction;
        Assert.HasCount(0, await f.Repository.AddSourceDemandsAsync([])); Assert.AreEqual(0, await f.Repository.HardDeleteForSourceCancellationAsync([]));
        Assert.AreEqual(0, await f.Repository.VoidRolledBackForSourceCancellationAsync([], "原原因"));
        Assert.AreSame(tx, f.Session.Transaction); Assert.AreEqual(query, await LastQuery(observer, f)); Assert.AreEqual(0L, await LockCount(observer, f));
    }

    /// <summary>九方法父事务缺少准备时均明确拒绝；拒绝不是回滚或释放父事务，实际SELECT1仍可用。</summary>
    [TestMethod]
    [DataRow("get-id")] [DataRow("get-source")] [DataRow("add")] [DataRow("source")] [DataRow("manual")]
    [DataRow("close")] [DataRow("delete")] [DataRow("void-ids")] [DataRow("void-source")]
    public async Task ParentWithoutPreparation_RejectsAllNineAndPreservesTransaction(string operation)
    {
        await using var f = new Fixture(); var row = Demand(type: "MD"); f.Begin(IsolationLevel.Serializable); var tx = f.Session.Transaction;
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Invoke(f, operation, row)); Assert.AreSame(tx, f.Session.Transaction);
        Assert.AreEqual(1, await f.Session.Connection.ExecuteScalarAsync<int>(new CommandDefinition("SELECT 1", transaction: tx)));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => f.Repository.AcquireBeforeTransactionAsync([row.SourceUid], [row.Uid]));
        Assert.AreSame(tx, f.Session.Transaction);
    }

    /// <summary>父事务有效租约仍不得追加未知来源或未声明缺失U；拒绝保留事务，已声明缺失U则可正常返回NULL。</summary>
    [TestMethod]
    public async Task ParentCoverage_DistinguishesSourceAndGrossIdentity()
    {
        await using var f = new Fixture(); var source = Guid.NewGuid(); var declared = Guid.NewGuid(); var undeclared = Guid.NewGuid();
        await f.PrepareBegin([source], [declared]); var tx = f.Session.Transaction;
        Assert.IsNull(await f.Repository.GetByIdForUpdateAsync(declared));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => f.Repository.GetByIdForUpdateAsync(undeclared));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => f.Repository.GetActiveBySourceForUpdateAsync("SO", Guid.NewGuid()));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => f.Repository.AddSourceDemandsAsync([Demand()]));
        Assert.AreSame(tx, f.Session.Transaction); Assert.AreEqual(1, await f.Session.Connection.ExecuteScalarAsync<int>("SELECT 1", transaction: tx));
    }

    /// <summary>只声明来源也会真实发现软删/全部生命周期成员；所有既有U与来源S由实际try-lock证明在事务前覆盖。</summary>
    [TestMethod]
    public async Task SourcePreparation_DiscoversAllMembersAndExistingGrossBinding()
    {
        await using var seed = new Fixture(); await using var prepared = new Fixture(); await using var observer = new Fixture();
        var a = Demand(); var b = Demand(a.SourceUid); b.IsDeleted = true; var c = Demand(a.SourceUid); c.LifecycleStatus = GrossDemandLifecycleStatusEnum.Closed;
        foreach (var row in new[] { a, b, c }) await seed.InsertCommitted(row);
        await prepared.Prepare([a.SourceUid], []);
        Assert.IsFalse(await TryKey(observer, "Source", a.SourceUid));
        foreach (var row in new[] { a, b, c }) Assert.IsFalse(await TryKey(observer, "Gross", row.Uid));
        prepared.Begin(IsolationLevel.RepeatableRead); AssertEntity(a, (await prepared.Repository.GetByIdForUpdateAsync(a.Uid))!);
        Assert.IsNull(await prepared.Repository.GetByIdForUpdateAsync(b.Uid)); AssertEntity(c, (await prepared.Repository.GetByIdForUpdateAsync(c.Uid))!);
        await prepared.Release(); Assert.IsTrue(await TryKey(observer, "Source", a.SourceUid));
        await prepared.Prepare([], [a.Uid]); Assert.IsFalse(await TryKey(observer, "Source", a.SourceUid));
        Assert.IsFalse(await TryKey(observer, "Gross", b.Uid)); Assert.IsFalse(await TryKey(observer, "Gross", c.Uid));
    }

    /// <summary>新身份可在已覆盖S的业务事务内生成，借真实事务U；仓储不生成第二个来源租约，事务结束前U不能被另一连接获取。</summary>
    [TestMethod]
    public async Task Add_NewIdentityAfterSourcePreparationBorrowsTransactionU()
    {
        await using var f = new Fixture(); await using var observer = new Fixture(); var row = Demand(); row.Uid = Guid.Empty;
        await f.PrepareBegin([row.SourceUid], []); AssertWrite((await f.Repository.AddSourceDemandsAsync([row])).Single(), row.Uid, null, 1, "新增成功。");
        Assert.AreNotEqual(Guid.Empty, row.Uid); Assert.IsFalse(await TryKey(observer, "Gross", row.Uid));
        f.End(false); Assert.IsTrue(await TryKey(observer, "Gross", row.Uid)); Assert.IsFalse(await TryKey(observer, "Source", row.SourceUid));
        await f.Release(); Assert.IsTrue(await TryKey(observer, "Source", row.SourceUid)); Assert.IsNull(await f.Read(row.Uid));
    }

    /// <summary>无父事务九方法以真实方法租约运行，不自行创建事务；物理提交行为及方法结束后S/U释放逐项验收。</summary>
    [TestMethod]
    [DataRow("get-id")] [DataRow("get-source")] [DataRow("add")] [DataRow("source")] [DataRow("manual")]
    [DataRow("close")] [DataRow("delete")] [DataRow("void-ids")] [DataRow("void-source")]
    public async Task NoParentTransaction_AllNineKeepCallerSessionTransactionNull(string operation)
    {
        await using var f = new Fixture(); await using var observer = new Fixture(); var row = Demand(type: "MD");
        if (operation != "add") await f.InsertCommitted(row); else f.Track(row.Uid);
        await Invoke(f, operation, row); Assert.IsNull(f.Session.Transaction);
        Assert.AreEqual(0L, await LockCount(observer, f)); Assert.IsTrue(await TryKey(observer, "Source", row.SourceUid)); Assert.IsTrue(await TryKey(observer, "Gross", row.Uid));
        var stored = await observer.Read(row.Uid);
        if (operation == "delete") Assert.IsNull(stored);
        else
        {
            Assert.IsNotNull(stored);
            var expectedVersion = operation is "source" or "manual" or "close" or "void-ids" or "void-source" ? 2 : 1;
            Assert.AreEqual(expectedVersion, stored.Version);
        }
    }

    /// <summary>真实来源读持S后，七写方法不靠父头或相同CommandId也互斥；独立源可前进且等待者尚未建立事务。</summary>
    [TestMethod]
    [DataRow("add")] [DataRow("source")] [DataRow("manual")] [DataRow("close")]
    [DataRow("delete")] [DataRow("void-ids")] [DataRow("void-source")]
    [Timeout(60000)]
    public async Task SameSource_AllSevenWritersActuallyWaitBeforeTransaction(string operation)
    {
        await using var seed = new Fixture(); await using var holder = new Fixture(); await using var writer = new Fixture(); await using var observer = new Fixture();
        var row = Demand(type: "MD"); if (operation != "add") await seed.InsertCommitted(row); else writer.Track(row.Uid);
        await holder.PrepareBegin([row.SourceUid], []); await holder.Repository.GetActiveBySourceForUpdateAsync("MD", row.SourceUid);
        Task? pending = null;
        try
        {
            pending = Invoke(writer, operation, row); using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await ObserveAttempt(observer, writer, deadline.Token); Assert.IsFalse(pending.IsCompleted); Assert.IsNull(writer.Session.Transaction);
            Assert.IsFalse(await TryKey(observer, "Source", row.SourceUid));
            Assert.IsTrue(await IdleOutsideTransaction(observer, writer));
            // 观察连接同时执行不同来源的真实新增，避免全模块串行也通过同源测试。
            var independent = Demand(type: "MD"); observer.Track(independent.Uid);
            AssertWrite((await observer.Repository.AddSourceDemandsAsync([independent])).Single(), independent.Uid, null, 1, "新增成功。");
            Assert.IsNull(observer.Session.Transaction); Assert.IsFalse(pending.IsCompleted);
            await holder.Release(); await pending.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.AreEqual(0L, await LockCount(observer, writer)); Assert.IsNull(writer.Session.Transaction);
            var stored = await observer.Read(row.Uid);
            if (operation == "delete") Assert.IsNull(stored);
            else { Assert.IsNotNull(stored); Assert.AreEqual(operation == "add" ? 1 : 2, stored.Version); }
        }
        finally { await holder.Release(); if (pending is not null) await pending; }
    }

    /// <summary>空来源仍保护全新身份，缺失UID则用独立U保护；Gross/Source/Line三种身份不会误用同一键。</summary>
    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    [Timeout(60000)]
    public async Task EmptySourceOrMissingUid_BlocksSpecifiedUidInsert(bool missingUid)
    {
        await using var holder = new Fixture(); await using var writer = new Fixture(); await using var observer = new Fixture(); var row = Demand(); writer.Track(row.Uid);
        Assert.AreNotEqual(row.SourceUid, row.Uid); Assert.AreNotEqual(row.SourceLineId, row.Uid);
        await holder.PrepareBegin(missingUid ? [] : [row.SourceUid], missingUid ? [row.Uid] : []);
        if (missingUid) Assert.IsNull(await holder.Repository.GetByIdForUpdateAsync(row.Uid));
        else Assert.HasCount(0, await holder.Repository.GetActiveBySourceForUpdateAsync("so ", row.SourceUid));
        Task<IReadOnlyList<RepositoryWriteResult>>? pending = null;
        try
        {
            pending = writer.Repository.AddSourceDemandsAsync([row]); using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await ObserveAttempt(observer, writer, deadline.Token); Assert.IsFalse(pending.IsCompleted);
            Assert.IsNull(writer.Session.Transaction); Assert.IsTrue(await IdleOutsideTransaction(observer, writer));
            Assert.IsFalse(await TryKey(observer, missingUid ? "Gross" : "Source", missingUid ? row.Uid : row.SourceUid));
            Assert.IsNull(await observer.Read(row.Uid)); await holder.Release();
            AssertWrite((await pending.WaitAsync(TimeSpan.FromSeconds(15))).Single(), row.Uid, null, 1, "新增成功。");
            AssertEntity(row, (await observer.Read(row.Uid))!); Assert.AreEqual(0L, await LockCount(observer, writer));
        }
        finally { await holder.Release(); if (pending is not null) await pending; }
    }

    /// <summary>同SourceUID但不同SourceType也共享S，不把字典文本当锁分区；SQL谓词仍区分MD与CR并返回真实空集合。</summary>
    [TestMethod, Timeout(60000)]
    public async Task SameSourceUidDifferentType_SharesResourceWithoutChangingPredicate()
    {
        await using var holder = new Fixture(); await using var waiter = new Fixture(); await using var observer = new Fixture(); var source = Guid.NewGuid();
        await holder.PrepareBegin([source], []); Assert.HasCount(0, await holder.Repository.GetActiveBySourceForUpdateAsync("MD", source));
        var pending = waiter.Repository.GetActiveBySourceForUpdateAsync("CR", source);
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10)); await ObserveAttempt(observer, waiter, deadline.Token);
            Assert.IsFalse(pending.IsCompleted); Assert.IsNull(waiter.Session.Transaction); Assert.IsFalse(await TryKey(observer, "Source", source));
            await holder.Release(); Assert.HasCount(0, await pending.WaitAsync(TimeSpan.FromSeconds(15)));
            Assert.AreEqual(0L, await LockCount(observer, waiter));
        }
        finally { await holder.Release(); await pending; }
    }

    /// <summary>真实执行器不同CommandId在beforeTransaction等S，首次快照随后建立；已提交来源版本可见而独立旧快照仍旧。</summary>
    [TestMethod]
    [DataRow(IsolationLevel.RepeatableRead)] [DataRow(IsolationLevel.Serializable)]
    [Timeout(60000)]
    public async Task Executor_HighIsolationWaitsBeforeSnapshotAndReadsCommittedVersion(IsolationLevel isolation)
    {
        await using var seed = new Fixture(); await using var holder = new Fixture(); await using var waiter = new Fixture(); await using var observer = new Fixture();
        var row = Demand(); await seed.InsertCommitted(row); var command = Guid.NewGuid(); using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        observer.Begin(IsolationLevel.RepeatableRead); Assert.AreEqual(1, (await observer.Read(row.Uid))!.Version);
        await holder.PrepareBegin([row.SourceUid], []); var changed = Clone(row); changed.Remark = "提交后的来源";
        AssertWrite(await holder.Repository.UpdateSourceDemandAsync(changed, 1), row.Uid, 1, 2, "来源需求已更新。");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var calls = 0; int? observed = null;
        Task<CommandExecutionResult<int>>? pending = null;
        try
        {
            pending = waiter.Executor.ExecuteAsync<int>(command, "__pgtest_gross_snapshot", row.SourceUid, new { row.Uid }, async token =>
            {
                Interlocked.Increment(ref calls); Assert.IsNotNull(waiter.Session.Transaction); Assert.AreEqual(isolation, waiter.Session.Transaction.IsolationLevel);
                observed = (await waiter.Repository.GetByIdForUpdateAsync(row.Uid))!.Version;
                Assert.AreEqual("提交后的来源", (await waiter.Read(row.Uid))!.Remark); throw new ProbeComplete();
            }, deadline.Token, isolation, token => { entered.TrySetResult(); return waiter.Repository.AcquireBeforeTransactionAsync([row.SourceUid], [], token); });
            await entered.Task.WaitAsync(deadline.Token); await ObserveAttempt(seed, waiter, deadline.Token);
            Assert.IsNull(waiter.Session.Transaction); Assert.IsFalse(pending.IsCompleted); Assert.AreEqual(0, calls);
            Assert.IsTrue(await IdleOutsideTransaction(seed, waiter)); Assert.IsFalse(await TryKey(seed, "Source", row.SourceUid));
            holder.End(true); await holder.Release();
            await Assert.ThrowsExactlyAsync<ProbeComplete>(() => pending); Assert.AreEqual(2, observed); Assert.AreEqual(1, calls);
            Assert.AreEqual(1, (await observer.Read(row.Uid))!.Version); Assert.IsNull(waiter.Session.Transaction);
            Assert.AreEqual(0L, await LockCount(seed, waiter));
            Assert.AreEqual(0L, await seed.Session.Connection.ExecuteScalarAsync<long>("SELECT count(*) FROM public.command_receipt WHERE command_id=@Command", new { Command = command }));
        }
        finally
        {
            await holder.Release(); deadline.Cancel();
            if (pending is not null) try { await pending; } catch (ProbeComplete) { } catch (OperationCanceledException) { }
        }
    }

    /// <summary>不同命令真实执行器都使用来源准备，第一委托失败回滚后第二才进入；回执/审计完整服务测试由主控另案负责。</summary>
    [TestMethod, Timeout(60000)]
    public async Task Executors_DifferentCommandIdsSerializeSameEmptySource()
    {
        await using var first = new Fixture(); await using var second = new Fixture(); await using var observer = new Fixture();
        var source = Guid.NewGuid(); var row = Demand(source); var one = Guid.NewGuid(); var two = Guid.NewGuid();
        var written = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20)); Task<CommandExecutionResult<int>>? secondTask = null;
        var firstTask = first.Executor.ExecuteAsync<int>(one, "__pgtest_gross_empty", source, new { row.Uid }, async token =>
        {
            Assert.HasCount(0, await first.Repository.GetActiveBySourceForUpdateAsync("SO", source));
            AssertWrite((await first.Repository.AddSourceDemandsAsync([row])).Single(), row.Uid, null, 1, "新增成功。");
            written.TrySetResult(); await finish.Task.WaitAsync(token); throw new ProbeComplete();
        }, deadline.Token, IsolationLevel.Serializable, token => first.Repository.AcquireBeforeTransactionAsync([source], [], token));
        try
        {
            await written.Task.WaitAsync(deadline.Token);
            secondTask = second.Executor.ExecuteAsync<int>(two, "__pgtest_gross_empty", source, new { row.Uid }, async _ =>
            {
                Assert.HasCount(0, await second.Repository.GetActiveBySourceForUpdateAsync("SO", source)); throw new ProbeComplete();
            }, deadline.Token, IsolationLevel.Serializable, token => second.Repository.AcquireBeforeTransactionAsync([source], [], token));
            await ObserveAttempt(observer, second, deadline.Token); Assert.IsNull(second.Session.Transaction); Assert.IsFalse(secondTask.IsCompleted);
            Assert.IsTrue(await IdleOutsideTransaction(observer, second)); Assert.IsFalse(await TryKey(observer, "Source", source));
            // 两不同命令均已各持机器锁，加上首请求S/U；两个backend确实有不同租约。
            Assert.IsTrue(await LockCount(observer, first) >= 3); Assert.AreEqual(1L, await LockCount(observer, second));
            finish.TrySetResult(); await Assert.ThrowsExactlyAsync<ProbeComplete>(() => firstTask); await Assert.ThrowsExactlyAsync<ProbeComplete>(() => secondTask);
            Assert.IsNull(await observer.Read(row.Uid)); Assert.AreEqual(0L, await LockCount(observer, first)); Assert.AreEqual(0L, await LockCount(observer, second));
            Assert.IsNull(first.Session.Transaction); Assert.IsNull(second.Session.Transaction);
        }
        finally
        {
            finish.TrySetResult(); deadline.Cancel();
            try { await firstTask; } catch (ProbeComplete) { } catch (OperationCanceledException) { }
            if (secondTask is not null) try { await secondTask; } catch (ProbeComplete) { } catch (OperationCanceledException) { }
        }
    }

    /// <summary>多来源反向输入仍统一先S后U；等待发生在事务外，释放后完整成员可锁读，不能因输入反向形成死锁。</summary>
    [TestMethod, Timeout(60000)]
    public async Task Preparation_ReversedMultiSourceInputsUseSameStableResources()
    {
        await using var seed = new Fixture(); await using var first = new Fixture(); await using var second = new Fixture(); await using var observer = new Fixture();
        var a = Demand(); var b = Demand(); await seed.InsertCommitted(a); await seed.InsertCommitted(b);
        await first.Prepare([a.SourceUid, b.SourceUid, a.SourceUid], [a.Uid, b.Uid, a.Uid]); Assert.AreEqual(4L, await LockCount(observer, first));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15)); Task<IAsyncDisposable?>? pending = null;
        try
        {
            pending = second.Repository.AcquireBeforeTransactionAsync([b.SourceUid, a.SourceUid], [b.Uid, a.Uid], deadline.Token);
            await ObserveAttempt(observer, second, deadline.Token); Assert.IsFalse(pending.IsCompleted); Assert.IsNull(second.Session.Transaction);
            Assert.AreEqual(0L, await LockCount(observer, second)); await first.Release();
            var lease = await pending; Assert.IsNotNull(lease); second.Adopt(lease); Assert.AreEqual(4L, await LockCount(observer, second));
            second.Begin(IsolationLevel.Serializable); AssertEntity(a, (await second.Repository.GetByIdForUpdateAsync(a.Uid))!); AssertEntity(b, (await second.Repository.GetByIdForUpdateAsync(b.Uid))!);
            await second.Release(); Assert.AreEqual(0L, await LockCount(observer, second));
        }
        finally
        {
            await first.Release(); deadline.Cancel();
            if (pending is not null) try { if (await pending is { } late) { second.End(false); await late.DisposeAsync(); } } catch (OperationCanceledException) { }
        }
    }

    /// <summary>锁前定位缺失变存在或既存身份改绑时，公开准备释放旧资源重新发现；测试注入直接SQL不冒充生产写者。</summary>
    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    [Timeout(60000)]
    public async Task Preparation_BindingChangedWhileWaitingReleasesAndDiscoversNewSource(bool rebind)
    {
        await using var holder = new Fixture(); await using var waiter = new Fixture(); await using var injector = new Fixture();
        var row = Demand(); var oldSource = row.SourceUid; var newSource = Guid.NewGuid();
        if (rebind) await injector.InsertCommitted(row);
        await holder.Prepare([], [row.Uid]);
        await injector.Prepare([newSource], []);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var pending = waiter.Repository.AcquireBeforeTransactionAsync([], [row.Uid], deadline.Token);
        try
        {
            await ObserveAttempt(injector, waiter, deadline.Token); Assert.IsFalse(pending.IsCompleted);
            Assert.IsNull(waiter.Session.Transaction);
            row.SourceUid = newSource;
            if (rebind)
                Assert.AreEqual(1, await injector.Session.Connection.ExecuteAsync("UPDATE public.gross_demand SET source_uid=@Source WHERE uid=@Uid", new { Source = newSource, row.Uid }));
            else await injector.InsertCommitted(row);
            await holder.Release();
            var releasedAt = await holder.Session.Connection.ExecuteScalarAsync<DateTime>("SELECT clock_timestamp()");
            // 等待新的S时旧U和旧S必须已经全部释放，不能持U后追加S形成倒序。
            while (true)
            {
                var newAttempt = await holder.Session.Connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                    "SELECT EXISTS(SELECT 1 FROM pg_stat_activity WHERE pid=@Pid AND query LIKE '%pg_try_advisory_lock%' AND query_start>@Since)",
                    new { waiter.Pid, Since = releasedAt }, cancellationToken: deadline.Token));
                if (newAttempt && await LockCount(holder, waiter) == 0 && await TryKey(holder, "Gross", row.Uid)) break;
                await Task.Delay(25, deadline.Token);
            }
            Assert.IsTrue(await TryKey(holder, "Source", oldSource)); Assert.IsFalse(await TryKey(holder, "Source", newSource));
            Assert.IsFalse(pending.IsCompleted); Assert.IsNull(waiter.Session.Transaction);
            await injector.Release(); var lease = await pending; Assert.IsNotNull(lease); waiter.Adopt(lease);
            Assert.IsFalse(await TryKey(holder, "Source", newSource)); Assert.IsFalse(await TryKey(holder, "Gross", row.Uid));
            waiter.Begin(IsolationLevel.Serializable); AssertEntity(row, (await waiter.Repository.GetByIdForUpdateAsync(row.Uid))!);
            await waiter.Release(); Assert.AreEqual(0L, await LockCount(holder, waiter));
        }
        finally
        {
            await holder.Release(); await injector.Release(); deadline.Cancel();
            try { if (await pending is { } late) { waiter.End(false); await late.DisposeAsync(); } } catch (OperationCanceledException) { }
        }
    }

    /// <summary>公开准备等待取消原样传播；已获取来源部分锁清理或隔离，取消不建立事务、写行或泄漏U。</summary>
    [TestMethod, Timeout(60000)]
    public async Task Preparation_ActualMissingUWaitCancellationLeavesNoLocks()
    {
        await using var holder = new Fixture(); await using var waiter = new Fixture(); await using var observer = new Fixture(); var row = Demand();
        await holder.Prepare([], [row.Uid]); using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var pid = waiter.Pid; var pending = waiter.Repository.AcquireBeforeTransactionAsync([row.SourceUid], [row.Uid], cancellation.Token);
        try
        {
            await ObserveAttempt(observer, waiter, cancellation.Token);
            while (await LockCount(observer, waiter) != 1) await Task.Delay(25, cancellation.Token);
            var sourceHeldAt = await observer.Session.Connection.ExecuteScalarAsync<DateTime>("SELECT clock_timestamp()");
            await ObserveAttemptAfter(observer, waiter, sourceHeldAt, cancellation.Token);
            Assert.IsFalse(await TryKey(observer, "Source", row.SourceUid)); Assert.IsNull(waiter.Session.Transaction);
            cancellation.Cancel(); await Assert.ThrowsAsync<OperationCanceledException>(() => pending);
            Assert.AreEqual(0L, await observer.Session.Connection.ExecuteScalarAsync<long>("SELECT count(*) FROM pg_locks WHERE pid=@Pid AND locktype='advisory'", new { Pid = pid }));
            Assert.IsTrue(await TryKey(observer, "Source", row.SourceUid)); Assert.IsNull(await observer.Read(row.Uid));
            if (waiter.Session.Connection.State == ConnectionState.Open) Assert.AreEqual(1, await waiter.Session.Connection.ExecuteScalarAsync<int>("SELECT 1"));
        }
        finally
        {
            cancellation.Cancel(); await holder.Release();
            try { if (await pending is { } late) await late.DisposeAsync(); } catch (OperationCanceledException) { }
        }
    }

    /// <summary>每例合法独立非标实体；日期精确到微秒、数量为正、三种来源身份独立，不依赖真实产品或客户外键。</summary>
    private static GrossDemand Demand(Guid? source = null, string type = "SO") => new()
    {
        Uid = Guid.NewGuid(), Version = 1, SourceType = type, SourceUid = source ?? Guid.NewGuid(), SourceLineId = Guid.NewGuid(),
        SourceNo = "PGG" + Guid.NewGuid().ToString("N"), SourceLineNumber = 1, SourceVersion = 2, DemandBasisFingerprint = "初始指纹",
        CustomerId = null, ProductId = null, IsNonStandard = true, SkuSnapshot = "PGG" + Guid.NewGuid().ToString("N"),
        LinkSkuSnapshot = "初始关联", CustomerSkuSnapshot = "初始客户型号", DescriptionSnapshot = "初始描述", ColorSnapshot = "初始颜色",
        SpecificationSnapshot = "初始规格", ImagePathSnapshot = "初始图片", UnitSnapshot = " PCS ", DemandQty = 5.125m, RequiredDate = Wall,
        Remark = "初始备注", CreatedDate = Wall.AddDays(-2), CreatedBy = "初始创建人", UpdatedDate = Wall.AddDays(-1), UpdatedBy = "初始更新人",
        LifecycleChangedAt = Wall.AddDays(-3), LifecycleChangedBy = "初始状态人", LifecycleChangedReason = "初始状态原因"
    };

    /// <summary>不使用生产映射来生成预期写列；浅复制全部实体属性用于检查排除字段。</summary>
    private static GrossDemand Clone(GrossDemand value)
    { var result = new GrossDemand(); foreach (var p in typeof(GrossDemand).GetProperties().Where(p => p.CanRead && p.CanWrite)) p.SetValue(result, p.GetValue(value)); return result; }
    /// <summary>只复制源协议明确的字段，避免预期结果跟随生产SET清单错误变化。</summary>
    private static void CopyProperty(GrossDemand from, GrossDemand to, string name)
    { var p = typeof(GrossDemand).GetProperty(name)!; p.SetValue(to, p.GetValue(from)); }
    /// <summary>逐属性完整比较持久化实体；PG墙上时间微秒截断仅处理驱动精度，不放宽业务值。</summary>
    private static void AssertEntity(GrossDemand expected, GrossDemand actual)
    {
        foreach (var p in typeof(GrossDemand).GetProperties().Where(p => p.CanRead && p.CanWrite))
        {
            var e = p.GetValue(expected); var a = p.GetValue(actual);
            if (e is DateTime date) e = FloorMicrosecond(date);
            Assert.AreEqual(e, a, p.Name);
        }
    }
    /// <summary>数据库timestamp(6)只丢失100ns尾数，固定夹具不产生墙上时区转换。</summary>
    private static DateTime FloorMicrosecond(DateTime value) => new(value.Ticks - value.Ticks % 10, DateTimeKind.Unspecified);
    /// <summary>核对原成功结果全部字段，不仅检查Success。</summary>
    private static void AssertWrite(RepositoryWriteResult result, Guid uid, int? oldVersion, int? newVersion, string message)
    { Assert.IsTrue(result.Success); Assert.AreEqual(uid, result.Uid); Assert.AreEqual(oldVersion, result.OldVersion); Assert.AreEqual(newVersion, result.NewVersion); Assert.AreEqual(1, result.AffectedRows); Assert.IsNull(result.ErrorCode); Assert.AreEqual(message, result.Message); }
    /// <summary>零行并发冲突保持原结果结构和消息。</summary>
    private static void AssertConflict(RepositoryWriteResult result, Guid uid, int version, string message)
    { Assert.IsFalse(result.Success); Assert.AreEqual(uid, result.Uid); Assert.AreEqual(version, result.OldVersion); Assert.IsNull(result.NewVersion); Assert.AreEqual(0, result.AffectedRows); Assert.AreEqual("CONCURRENCY_CONFLICT", result.ErrorCode); Assert.AreEqual(message, result.Message); }
    /// <summary>生命周期命令仅允许状态、审计和Version变化，两个时间列必须同一微秒值。</summary>
    private static void AssertLifecycle(GrossDemand old, GrossDemand stored, GrossDemandLifecycleStatusEnum status, string? reason)
    {
        var expected = Clone(old); expected.Version++; expected.LifecycleStatus = status; expected.LifecycleChangedReason = reason;
        expected.UpdatedBy = expected.LifecycleChangedBy = TestUser.Name; expected.UpdatedDate = expected.LifecycleChangedAt = stored.UpdatedDate;
        Assert.IsNotNull(stored.UpdatedDate); AssertEntity(expected, stored);
    }

    /// <summary>九方法统一调用仅组织测试参数，保留真实仓储SQL及真实返回断言。</summary>
    private static async Task Invoke(Fixture f, string operation, GrossDemand row)
    {
        switch (operation)
        {
            case "get-id": Assert.IsNotNull(await f.Repository.GetByIdForUpdateAsync(row.Uid)); break;
            case "get-source": Assert.HasCount(1, await f.Repository.GetActiveBySourceForUpdateAsync(row.SourceType, row.SourceUid)); break;
            case "add": AssertWrite((await f.Repository.AddSourceDemandsAsync([row])).Single(), row.Uid, null, row.Version, "新增成功。"); break;
            case "source": AssertWrite(await f.Repository.UpdateSourceDemandAsync(row, row.Version), row.Uid, row.Version, row.Version + 1, "来源需求已更新。"); break;
            case "manual": AssertWrite(await f.Repository.UpdateManualAsync(row, row.Version), row.Uid, row.Version, row.Version + 1, "手工毛需求更新成功。"); break;
            case "close": AssertWrite(await f.Repository.CloseManualAsync(row.Uid, row.Version, "原原因"), row.Uid, row.Version, row.Version + 1, "手工毛需求已关闭。"); break;
            case "delete": Assert.AreEqual(1, await f.Repository.HardDeleteForSourceCancellationAsync([row.Uid])); break;
            case "void-ids": Assert.AreEqual(1, await f.Repository.VoidRolledBackForSourceCancellationAsync([row.Uid], "原原因")); break;
            case "void-source": Assert.AreEqual(1, await f.Repository.VoidPendingBySourceAsync(row.SourceType, row.SourceUid, "原原因")); break;
            default: Assert.Fail("未知测试方法。"); break;
        }
    }

    /// <summary>独立计算冻结协议键；真实pg_try_advisory_lock用于验证S/U覆盖，不反射或伪造生产租约。</summary>
    private static long Key(string family, Guid uid) => BinaryPrimitives.ReadInt64BigEndian(SHA256.HashData(Encoding.UTF8.GetBytes($"SwiftExport:SourceDemand:{family}:{uid:N}")));
    /// <summary>观察者成功取锁立即对称解锁，既证明物理互斥又不在观察连接遗留可重入计数。</summary>
    private static async Task<bool> TryKey(Fixture observer, string family, Guid uid)
    {
        var key = Key(family, uid); var ok = await observer.Session.Connection.ExecuteScalarAsync<bool>(new CommandDefinition("SELECT pg_try_advisory_lock(@Key)", new { Key = key }, observer.Session.Transaction));
        if (ok) Assert.IsTrue(await observer.Session.Connection.ExecuteScalarAsync<bool>(new CommandDefinition("SELECT pg_advisory_unlock(@Key)", new { Key = key }, observer.Session.Transaction)));
        return ok;
    }
    /// <summary>实际目录证明目标backend已执行生产try-lock；不把启动任务或固定延时当作等待证据。</summary>
    private static async Task ObserveAttempt(Fixture observer, Fixture target, CancellationToken token)
    {
        while (!await observer.Session.Connection.ExecuteScalarAsync<bool>(new CommandDefinition("SELECT EXISTS(SELECT 1 FROM pg_stat_activity WHERE pid=@Pid AND query LIKE '%pg_try_advisory_lock%')", new { target.Pid }, observer.Session.Transaction, cancellationToken: token)))
            await Task.Delay(25, token);
    }
    /// <summary>已持S后观察新一轮服务器try-lock，证明真正尝试被其他连接持有的U后才取消。</summary>
    private static async Task ObserveAttemptAfter(Fixture observer, Fixture target, DateTime since, CancellationToken token)
    {
        while (!await observer.Session.Connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS(SELECT 1 FROM pg_stat_activity WHERE pid=@Pid AND query LIKE '%pg_try_advisory_lock%' AND query_start>@Since)",
            new { target.Pid, Since = since }, observer.Session.Transaction, cancellationToken: token)))
            await Task.Delay(25, token);
    }
    /// <summary>等待轮询停在事务外时xact_start为NULL；仅采样到idle才返回true，排除单条自动提交锁查询的瞬时事务。</summary>
    private static async Task<bool> IdleOutsideTransaction(Fixture observer, Fixture target)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        while (true)
        {
            var row = await observer.Session.Connection.QuerySingleAsync(new CommandDefinition("SELECT state AS \"State\",xact_start IS NULL AS \"Outside\" FROM pg_stat_activity WHERE pid=@Pid", new { target.Pid }, observer.Session.Transaction, cancellationToken: deadline.Token));
            if ((string)row.State == "idle") return (bool)row.Outside;
            await Task.Delay(25, deadline.Token);
        }
    }
    /// <summary>用PID检查来源及命令所有advisory已释放，不依赖逻辑连接关闭。</summary>
    private static Task<long> LockCount(Fixture observer, Fixture target) => observer.Session.Connection.ExecuteScalarAsync<long>(new CommandDefinition("SELECT count(*) FROM pg_locks WHERE pid=@Pid AND locktype='advisory'", new { target.Pid }, observer.Session.Transaction));
    /// <summary>空方法前后读取服务端最近查询，确认没有租约发现、锁或业务SQL。</summary>
    private static Task<string?> LastQuery(Fixture observer, Fixture target) => observer.Session.Connection.ExecuteScalarAsync<string>("SELECT query FROM pg_stat_activity WHERE pid=@Pid", new { target.Pid });

    /// <summary>明确在回执Insert之前终止验收委托，不产生需删除的不可变回执；完整服务回滚由主控独占另案。</summary>
    private sealed class ProbeComplete : Exception { }
    /// <summary>随机认证身份只用于真实执行器和审计字段，不创建系统用户。</summary>
    private sealed class TestUser : ICurrentUser
    {
        /// <summary>固定非业务测试审计名称。</summary>
        public const string Name = "__pgtest_gross_document__";
        public Guid? UserId { get; } = Guid.NewGuid();
        public string UserName => Name;
        public bool IsAuthenticated => true;
    }

    /// <summary>真实Npgsql会话与公开准备入口；先释放父事务再归还租约，仅清理本人预登记的随机UID。</summary>
    private sealed class Fixture : IAsyncDisposable
    {
        private readonly HashSet<Guid> committed = [];
        private IAsyncDisposable? lease;
        public DbSession Session { get; }
        public RepositoryBaseContext Context { get; }
        public PostgreSqlGrossDemandDocumentRepository Repository { get; }
        public BusinessCommandExecutor Executor { get; }
        public int Pid => ((NpgsqlConnection)Session.Connection).ProcessID;

        /// <summary>连接前先过固定failure门禁，未启用即Inconclusive；不读取或打印凭据。</summary>
        public Fixture()
        {
            Session = new(new PostgreSqlConnectionFactory(PostgreSqlIntegrationEnvironment.GetFailureConnectionString()));
            var user = new TestUser(); Context = new(Session, NullLoggerFactory.Instance, new AuditStampService(user), user);
            Repository = new(Context); Executor = new(new ServiceBaseContext(Session, NullLoggerFactory.Instance), new PostgreSqlCommandReceiptRepository(Context), user);
        }
        /// <summary>新增提交前登记精确UID，包括稍后可能失败的新增；不按来源或SKU扩大删除范围。</summary>
        public void Track(Guid uid) { Assert.AreNotEqual(Guid.Empty, uid); committed.Add(uid); }
        /// <summary>只能接管本连接公开准备成功返回的真实租约，禁止覆盖或注入占位。</summary>
        public void Adopt(IAsyncDisposable value) { Assert.IsNull(lease); lease = value; }
        /// <summary>先真实准备所有来源及身份，之后才由本例建立父事务。</summary>
        public async Task PrepareBegin(Guid[] sources, Guid[] ids, IsolationLevel isolation = IsolationLevel.ReadCommitted)
        { await Prepare(sources, ids); Begin(isolation); }
        /// <summary>公开来源准备不能处于事务中，空集合仍使用真实返回租约。</summary>
        public async Task Prepare(Guid[] sources, Guid[] ids)
        { var value = await Repository.AcquireBeforeTransactionAsync(sources, ids); Assert.IsNotNull(value); Adopt(value); }
        /// <summary>测试自己拥有事务，故意无准备场景由测试明确调用此入口。</summary>
        public void Begin(IsolationLevel isolation) { Assert.IsNull(Session.Transaction); Session.Transaction = Session.Connection.BeginTransaction(isolation); }
        /// <summary>真实提交或回滚，只结束本例父事务并立即清空共享引用。</summary>
        public void End(bool commit)
        {
            if (Session.Transaction is not { } tx) return;
            try { if (commit) tx.Commit(); else tx.Rollback(); }
            finally { Session.Transaction = null; tx.Dispose(); }
        }
        /// <summary>总是先结束父事务再释放来源租约，不靠关闭连接证明解锁。</summary>
        public async Task Release() { End(false); if (lease is { } value) { lease = null; await value.DisposeAsync(); } }
        /// <summary>直接种入合法实体保留软删/状态/全部字段，用于隔离仓储INSERT常量与读取条件。</summary>
        public async Task Insert(GrossDemand row)
        {
            var columns = string.Join(',', Map.Columns.Select(c => PostgreSqlNames.Quote(c.ColumnName)));
            var values = string.Join(',', Map.Columns.Select(c => "@" + c.ParameterName));
            Assert.AreEqual(1, await Session.Connection.ExecuteAsync(new CommandDefinition($"INSERT INTO public.gross_demand ({columns}) VALUES ({values})", PostgreSqlParameters.Entity(row, Map), Session.Transaction)));
        }
        /// <summary>跨连接专用种子明确自动提交且登记UID，不能在回滚夹具内部偷偷提交。</summary>
        public async Task InsertCommitted(GrossDemand row) { Assert.IsNull(Session.Transaction); Track(row.Uid); await Insert(row); }
        /// <summary>原始全字段读取不筛软删，以核对被排除行是否受到意外修改。</summary>
        public Task<GrossDemand?> Read(Guid uid)
        {
            var columns = string.Join(',', Map.Columns.Select(c => PostgreSqlNames.Quote(c.ColumnName) + " AS " + PostgreSqlNames.Quote(c.PropertyName)));
            return Session.Connection.QuerySingleOrDefaultAsync<GrossDemand>(new CommandDefinition($"SELECT {columns} FROM public.gross_demand WHERE uid=@Uid", new { Uid = uid }, Session.Transaction));
        }
        /// <summary>结束全部自有资源后精确清理已登记Gross；不用来源范围DELETE，不碰审计/回执/序列或任何历史表。</summary>
        public async ValueTask DisposeAsync()
        {
            try
            {
                await Release();
                if (committed.Count > 0)
                {
                    // 独立清理连接处理测试会话已隔离的情况；实例仍通过同一固定failure门禁。
                    using var cleanup = new DbSession(new PostgreSqlConnectionFactory(PostgreSqlIntegrationEnvironment.GetFailureConnectionString()));
                    await cleanup.Connection.ExecuteAsync(new CommandDefinition("DELETE FROM public.gross_demand WHERE uid=ANY(@Uids)", PostgreSqlParameters.Ids(committed), commandTimeout: 10));
                    Assert.AreEqual(0L, await cleanup.Connection.ExecuteScalarAsync<long>(new CommandDefinition("SELECT count(*) FROM public.gross_demand WHERE uid=ANY(@Uids)", PostgreSqlParameters.Ids(committed))));
                }
            }
            finally { Session.Dispose(); }
        }
    }
}
