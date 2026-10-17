using System.Data;
using System.Text.Json;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Services.Base;
using SwiftExport.Application.Services.Products;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces;
using SwiftExport.Core.Interfaces.Data;
using SwiftExport.Core.Interfaces.Products.Catalog;

namespace SwiftExport.Application.Tests.Products;

/// <summary>直接调用 BOM 服务单条入口，证明它们共享最终图保存与事务内核；不连接数据库或替代实库并发验收。</summary>
[TestClass]
public sealed class BomSaveEntryTests
{
    /// <summary>组成的新增、修改及批量保存均检查所有共享父，不能只核对物理行的来源父。</summary>
    [TestMethod]
    [DataRow("零件", "add")] [DataRow("虚拟件", "add")] [DataRow("客供件", "add")] [DataRow("包材", "add")]
    [DataRow("零件", "update")] [DataRow("虚拟件", "update")] [DataRow("客供件", "update")] [DataRow("包材", "update")]
    [DataRow("零件", "batch")] [DataRow("虚拟件", "batch")] [DataRow("客供件", "batch")] [DataRow("包材", "batch")]
    public async Task ParentEligibility_SharedIllegalParentRejectsComposition(string material, string entry)
    {
        var fixture = new Fixture();
        var source = fixture.Product("SOURCE", "SHARED");
        var other = fixture.Product("OTHER", "SHARED");
        other.MaterialType = material;
        var child = fixture.Product("LEAF");
        var line = fixture.Line(source, child, 1);
        var before = fixture.PersistedState();
        bool success;
        if (entry == "batch")
            success = (await fixture.Service.BatchSaveAsync(new() { UpdateItems = [new() { Uid = line.Uid, Version = line.Version, Data = Dto(source, child) }] })).Success;
        else if (entry == "update")
            success = (await fixture.Entry.UpdateAsync(new() { Uid = line.Uid, Version = line.Version, Data = Dto(source, child) })).Success;
        else success = (await fixture.Entry.AddAsync(new() { Data = Dto(source, fixture.Product("NEW")) })).Success;
        Assert.IsFalse(success);
        Assert.HasCount(0, fixture.Writes);
        Assert.AreEqual(before, fixture.PersistedState());
        fixture.AssertOwnedTransaction(0, 1);
    }

    /// <summary>三种供货成品均可维护组成，五种物料仍可作为终端子件，不新增成品子件限制。</summary>
    [TestMethod]
    [DataRow("生产", "成品")] [DataRow("采购", "成品")] [DataRow("外协", "成品")]
    [DataRow("采购", "零件")] [DataRow("采购", "虚拟件")] [DataRow("采购", "客供件")] [DataRow("采购", "包材")]
    public async Task ParentEligibility_ValidParentsAndChildrenRemainUsable(string supply, string childMaterial)
    {
        var fixture = new Fixture();
        var parent = fixture.Product("PARENT", "SHARED");
        parent.SupplyType = supply;
        parent.IsActive = false;
        var child = fixture.Product("CHILD");
        child.MaterialType = childMaterial;
        var result = await fixture.Entry.AddAsync(new() { Data = Dto(parent, child) });
        Assert.IsTrue(result.Success, result.Message);
        Assert.HasCount(1, fixture.Writes);
        fixture.AssertOwnedTransaction(1, 0);
    }

    /// <summary>共享组成必须对全部有效父项展开，自环和跨共享组循环不能只检查来源父。</summary>
    [TestMethod]
    [DataRow("shared-self", "不能相同")]
    [DataRow("shared-cycle", "循环依赖")]
    [DataRow("shared-parent-role", "客供件")]
    public async Task Add_SharedParentsCannotBypassFinalGraph(string scenario, string expectedMessage)
    {
        var fixture = new Fixture();
        var source = fixture.Product("SOURCE", "SHARED");
        var other = fixture.Product("OTHER", "SHARED");
        other.BomVersion = 7;
        var child = fixture.Product("LEAF");
        if (scenario == "shared-self") child = other;
        if (scenario == "shared-cycle")
        {
            child.BomNo = "DOWNSTREAM";
            child.BomVersion = 1;
            fixture.Line(child, other, 1);
        }
        if (scenario == "shared-parent-role") other.MaterialType = "客供件";
        var before = fixture.PersistedState();
        var result = await fixture.Entry.AddAsync(new() { Data = Dto(source, child) });
        Assert.IsFalse(result.Success);
        Assert.AreEqual(ServiceErrorCodes.BusinessBlocked, result.Error.Code);
        StringAssert.Contains(result.Message, expectedMessage);
        Assert.HasCount(0, fixture.Writes);
        Assert.AreEqual(before, fixture.PersistedState());
        fixture.AssertOwnedTransaction(0, 1);
    }

    /// <summary>不同正标记的父项按编号共享，一次新增仅持久化一条组成，不复制已有物理行。</summary>
    [TestMethod]
    public async Task Add_SharedNumberWritesOnePhysicalLine()
    {
        var fixture = new Fixture();
        var source = fixture.Product("SOURCE", "SHARED");
        var other = fixture.Product("OTHER", "shared ");
        other.BomVersion = 9;
        var existing = fixture.Line(source, fixture.Product("FIRST"), 4);
        var oldSnapshot = Clone(existing);
        var result = await fixture.Entry.AddAsync(new() { Data = Dto(source, fixture.Product("SECOND")) });
        Assert.IsTrue(result.Success, result.Message);
        Assert.HasCount(1, fixture.Writes);
        Assert.HasCount(2, fixture.Rows);
        Assert.AreEqual(1, fixture.Writes.Single().Row.BomVersion);
        Assert.AreEqual(5, fixture.Writes.Single().Row.LineNumber);
        Assert.AreEqual(System.Text.Json.JsonSerializer.Serialize(oldSnapshot),
            System.Text.Json.JsonSerializer.Serialize(fixture.Rows[existing.Uid]));
        var facts = SharedBomTestFacts.Build(fixture.Products.Values, fixture.Rows.Values, []);
        Assert.HasCount(2, facts.Single().Products);
        Assert.HasCount(2, facts.Single().Lines);
        fixture.AssertOwnedTransaction(1, 0);
    }

    /// <summary>来源父失效不改变编号共享归属，修改仍保留来源身份和快照，不重写为当前引用者。</summary>
    [TestMethod]
    [DataRow("inactive")]
    [DataRow("deleted")]
    [DataRow("unbound")]
    [DataRow("other-number")]
    public async Task Update_ObsoleteSourceParentDoesNotBlockSharedGroup(string scenario)
    {
        var fixture = new Fixture();
        var source = fixture.Product("OLD-SOURCE", "SHARED");
        var current = fixture.Product("CURRENT", "SHARED");
        var child = fixture.Product("CHILD");
        var line = fixture.Line(source, child, 1);
        line.BomVersion = 7;
        current.BomVersion = 3;
        if (scenario == "inactive") source.IsActive = false;
        if (scenario == "deleted") source.IsDeleted = true;
        if (scenario == "unbound") { source.BomVersion = 0; source.BomNo = null; }
        if (scenario == "other-number") source.BomNo = "OTHER";
        var dto = Dto(current, child);
        dto.Numerator = 3;
        var result = await fixture.Entry.UpdateAsync(new() { Uid = line.Uid, Version = line.Version, Data = dto });
        Assert.IsTrue(result.Success, result.Message);
        Assert.HasCount(1, fixture.Writes);
        var saved = fixture.Rows[line.Uid];
        Assert.AreEqual(source.Uid, saved.ParentProductId);
        Assert.AreEqual("OLD-SOURCE", saved.ParentSkuSnapshot);
        Assert.AreEqual("SHARED", saved.BomNo);
        Assert.AreEqual(7, saved.BomVersion);
        Assert.AreEqual(3, saved.Numerator);
        fixture.AssertOwnedTransaction(1, 0);
    }

    /// <summary>触及异常未删物理行时显式拒绝，不按父正标记过滤旧行，也不顺手修正旧标记。</summary>
    [TestMethod]
    [DataRow(0)]
    [DataRow(-2)]
    public async Task Add_AbnormalSharedLineMarkerIsRejected(int marker)
    {
        var fixture = new Fixture();
        var parent = fixture.Product("P", "SHARED");
        fixture.Line(parent, fixture.Product("OLD"), 1).BomVersion = marker;
        var child = fixture.Product("NEW");
        var before = fixture.PersistedState();
        var result = await fixture.Entry.AddAsync(new() { Data = Dto(parent, child) });
        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.Message, "异常组成有效标记");
        Assert.AreEqual(ServiceErrorCodes.BusinessBlocked, result.Error.Code);
        Assert.HasCount(0, fixture.Writes);
        Assert.AreEqual(before, fixture.PersistedState());
        fixture.AssertOwnedTransaction(0, 1);
    }

    /// <summary>共享事实读取后锁定父记录发现版本变化时返回明确冲突，不依据过期父角色继续写组成。</summary>
    [TestMethod]
    public async Task Add_ParentVersionChangedAfterFactsReturnsConflict()
    {
        var fixture = new Fixture();
        var parent = fixture.Product("P", "SHARED");
        fixture.ChangeParentVersionAfterFacts = parent.Uid;
        var child = fixture.Product("CHILD");
        var before = fixture.PersistedState();
        var result = await fixture.Entry.AddAsync(new() { Data = Dto(parent, child) });
        Assert.IsFalse(result.Success);
        Assert.AreEqual(ServiceErrorCodes.VersionConflict, result.Error.Code);
        Assert.HasCount(0, fixture.Writes);
        Assert.AreEqual(before, fixture.PersistedState());
        fixture.AssertOwnedTransaction(0, 1);
    }

    /// <summary>任意正父标记均可维护共享组成，新增行写一但不改写引用父标记。</summary>
    [TestMethod]
    public async Task Add_OnlyPositiveParentIsAccepted()
    {
        var fixture = new Fixture();
        var parent = fixture.Product("P", "SHARED");
        parent.BomVersion = 3;
        var child = fixture.Product("CHILD");
        var result = await fixture.Entry.AddAsync(new() { Data = Dto(parent, child) });
        Assert.IsTrue(result.Success, result.Message);
        Assert.HasCount(1, fixture.Writes);
        Assert.AreEqual(1, fixture.Writes.Single().Row.BomVersion);
        Assert.AreEqual(3, parent.BomVersion);
        fixture.AssertOwnedTransaction(1, 0);
    }

    /// <summary>已有正数组成与父标记无需数值相同，追加时原物理行全部字段保持原值。</summary>
    [TestMethod]
    [DataRow(2)]
    [DataRow(7)]
    public async Task Add_PositiveLineMarkersAreEquivalentAndPreserved(int marker)
    {
        var fixture = new Fixture();
        var parent = fixture.Product("P", "SHARED");
        parent.BomVersion = 3;
        var existing = fixture.Line(parent, fixture.Product("OLD"), 4);
        existing.BomVersion = marker;
        var before = JsonSerializer.Serialize(existing);
        var result = await fixture.Entry.AddAsync(new() { Data = Dto(parent, fixture.Product("NEW")) });
        Assert.IsTrue(result.Success, result.Message);
        Assert.HasCount(1, fixture.Writes);
        Assert.AreEqual(5, fixture.Writes.Single().Row.LineNumber);
        Assert.AreEqual(1, fixture.Writes.Single().Row.BomVersion);
        Assert.AreEqual(before, JsonSerializer.Serialize(fixture.Rows[existing.Uid]));
        fixture.AssertOwnedTransaction(1, 0);
    }

    /// <summary>基础服务引用也必须执行最终图校验；重复、自环、非法比例及跨层成环均回滚且不写行。</summary>
    [TestMethod]
    [DataRow("duplicate", "重复")]
    [DataRow("self", "不能相同")]
    [DataRow("numerator", "大于零")]
    [DataRow("denominator", "大于零")]
    [DataRow("cycle", "循环依赖")]
    public async Task Add_RejectsInvalidFinalGraphWithoutWrites(string scenario, string expectedMessage)
    {
        var fixture = new Fixture();
        var parent = fixture.Product("A", "BOM-A");
        var child = fixture.Product("B");
        var dto = Dto(parent, child);
        if (scenario == "duplicate") fixture.Line(parent, child, 1);
        if (scenario == "self") dto.SubSkuId = parent.Uid;
        if (scenario == "numerator") dto.Numerator = 0;
        if (scenario == "denominator") dto.Denominator = 0;
        if (scenario == "cycle")
        {
            child.BomNo = "BOM-B";
            child.BomVersion = 1;
            var third = fixture.Product("C", "BOM-C");
            fixture.Line(child, third, 1);
            fixture.Line(third, parent, 1);
        }
        var before = fixture.PersistedState();
        var result = await fixture.Entry.AddAsync(new() { Data = dto, ClientKey = "single-create", RowIndex = 7 });
        Assert.IsFalse(result.Success);
        Assert.AreEqual(ServiceErrorCodes.BusinessBlocked, result.Error.Code);
        StringAssert.Contains(result.Message, expectedMessage);
        Assert.HasCount(0, fixture.Writes);
        Assert.AreEqual(before, fixture.PersistedState());
        fixture.AssertOwnedTransaction(commits: 0, rollbacks: 1);
        if (scenario is "duplicate" or "self" or "cycle") Assert.IsTrue(fixture.GraphReads > 0);
    }

    /// <summary>合法单条新增按共享组分配行号与标准有效标记，父正标记不作为组成版本，回执保留客户端定位。</summary>
    [TestMethod]
    public async Task Add_UsesCurrentGraphAndCommitsOneTransaction()
    {
        var fixture = new Fixture();
        var parent = fixture.Product("A", "BOM-A");
        parent.BomVersion = 3;
        fixture.Product("STANDARD-REFERENCE", "BOM-A");
        fixture.Line(parent, fixture.Product("B"), 5);
        var child = fixture.Product("C");
        var result = await fixture.Entry.AddAsync(new()
        {
            ClientKey = "create-key", RowIndex = 9,
            Data = new() { BomNo = " BOM-A ", SubSkuId = child.Uid, SubSku = "untrusted-snapshot", Numerator = 2, Denominator = 3 }
        });
        Assert.IsTrue(result.Success, result.Message);
        Assert.IsTrue(result.Data.Success);
        Assert.AreEqual("create-key", result.Data.ClientKey);
        Assert.AreEqual(9, result.Data.RowIndex);
        var written = fixture.Writes.Single().Row;
        Assert.AreEqual("AddAsync", fixture.Writes.Single().Operation);
        Assert.IsTrue(fixture.Products.Values.Any(product => product.Uid == written.ParentProductId
            && product.Sku == written.ParentSkuSnapshot && product.BomNo == "BOM-A"));
        Assert.AreEqual(1, written.BomVersion);
        Assert.AreEqual(6, written.LineNumber);
        Assert.AreEqual(child.Uid, written.ComponentProductId);
        Assert.AreEqual(child.Sku, written.ComponentSkuSnapshot);
        Assert.AreEqual(2, written.Numerator);
        Assert.AreEqual(3, written.Denominator);
        Assert.IsTrue(fixture.GraphReads > 0);
        fixture.AssertOwnedTransaction(1, 0);
    }

    /// <summary>更新以 wrapper Version 为唯一并发输入；DTO 的旧身份、版本和组件文本不能覆盖服务端身份。</summary>
    [TestMethod]
    [DataRow(5, true)]
    [DataRow(4, false)]
    public async Task Update_UsesWrapperVersionAndAllowsValidComponentReplacement(int wrapperVersion, bool expectedSuccess)
    {
        var fixture = new Fixture();
        var parent = fixture.Product("A", "BOM-A");
        var oldChild = fixture.Product("B");
        var newChild = fixture.Product("C");
        var line = fixture.Line(parent, oldChild, 4);
        line.Version = 5;
        var before = fixture.PersistedState();
        var dto = Dto(parent, newChild);
        dto.Uid = Guid.NewGuid();
        dto.Version = 999;
        dto.SubSku = "untrusted-new-code";
        var result = await fixture.Entry.UpdateAsync(new()
        { Uid = line.Uid, Version = wrapperVersion, Data = dto, ClientKey = "update-key", RowIndex = 2 });
        Assert.AreEqual(expectedSuccess, result.Success, result.Message);
        if (expectedSuccess)
        {
            var write = fixture.Writes.Single();
            Assert.AreEqual("UpdateAsync", write.Operation);
            Assert.AreEqual(line.Uid, write.Row.Uid);
            Assert.AreEqual(5, write.Row.Version);
            Assert.AreEqual(4, write.Row.LineNumber);
            Assert.AreEqual(newChild.Uid, write.Row.ComponentProductId);
            Assert.AreEqual(newChild.Sku, write.Row.ComponentSkuSnapshot);
            Assert.AreEqual(6, fixture.Rows[line.Uid].Version);
            Assert.AreEqual(5, result.Data.OldVersion);
            Assert.AreEqual(6, result.Data.NewVersion);
            Assert.AreEqual("update-key", result.Data.ClientKey);
            Assert.IsTrue(fixture.GraphReads > 0);
        }
        else
        {
            Assert.AreEqual(ServiceErrorCodes.VersionConflict, result.Error.Code);
            Assert.IsNull(result.Data);
            StringAssert.Contains(result.Message, "已被其他人修改");
            Assert.HasCount(0, fixture.Writes);
            Assert.AreEqual(before, fixture.PersistedState());
        }
        fixture.AssertOwnedTransaction(expectedSuccess ? 1 : 0, expectedSuccess ? 0 : 1);
    }

    /// <summary>软硬删除的明确版本预检失败均返回版本冲突，在持久写入前拒绝且不携带行回执。</summary>
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task Delete_StaleWrapperVersionIsConflictBeforeWrites(bool soft)
    {
        var fixture = new Fixture();
        var parent = fixture.Product("A", "BOM-A");
        var line = fixture.Line(parent, fixture.Product("B"), 1);
        fixture.Line(parent, fixture.Product("C"), 2);
        line.Version = 5;
        var before = fixture.PersistedState();
        var result = await fixture.Entry.DeleteAsync(new() { Uid = line.Uid, Version = 4, IsSoftDelete = soft });
        Assert.IsFalse(result.Success);
        Assert.AreEqual(ServiceErrorCodes.VersionConflict, result.Error.Code);
        Assert.IsNull(result.Data);
        Assert.HasCount(0, fixture.Writes);
        Assert.AreEqual(before, fixture.PersistedState());
        fixture.AssertOwnedTransaction(0, 1);
    }

    /// <summary>基础服务单项入口保留提交结果未知；提交后确认丢失不能被回滚假造撤销，也不能自动重写。</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Update_CommitFailurePreservesUnknownOutcomeAndActualFacts(bool committed)
    {
        var fixture = new Fixture();
        fixture.Session.ConnectionStub.FailCommitBefore = !committed;
        fixture.Session.ConnectionStub.FailCommitAfter = committed;
        var parent = fixture.Product("A", "BOM-A");
        var child = fixture.Product("B");
        var line = fixture.Line(parent, child, 1);
        var dto = Dto(parent, child);
        dto.Numerator = 2;
        var before = fixture.PersistedState();
        var result = await fixture.Entry.UpdateAsync(new() { Uid = line.Uid, Version = line.Version, Data = dto });
        Assert.IsFalse(result.Success);
        Assert.AreEqual(ServiceErrorCodes.OutcomeUnknown, result.Error.Code);
        Assert.IsNull(result.Data);
        Assert.IsFalse(result.Message.Contains("已回滚", StringComparison.Ordinal));
        Assert.IsFalse(result.Message.Contains("CommandId", StringComparison.OrdinalIgnoreCase));
        Assert.AreEqual(1, fixture.Writes.Count);
        Assert.AreEqual(committed, fixture.Session.ConnectionStub.Last!.IsCommitted);
        if (committed)
        {
            Assert.AreNotEqual(before, fixture.PersistedState());
            Assert.AreEqual(2, fixture.Rows[line.Uid].Numerator);
            Assert.AreEqual(line.Version + 1, fixture.Rows[line.Uid].Version);
        }
        else Assert.AreEqual(before, fixture.PersistedState());
        fixture.AssertOwnedTransaction(1, 1);
    }

    /// <summary>修改请求若形成重复组件，最终图拒绝且持久实体不被准备阶段的原地修改污染。</summary>
    [TestMethod]
    public async Task Update_RejectedGraphDoesNotMutatePersistedRows()
    {
        var fixture = new Fixture();
        var parent = fixture.Product("A", "BOM-A");
        var firstChild = fixture.Product("B");
        var secondChild = fixture.Product("C");
        var target = fixture.Line(parent, firstChild, 1);
        fixture.Line(parent, secondChild, 2);
        var before = fixture.PersistedState();
        var result = await fixture.Entry.UpdateAsync(new()
        { Uid = target.Uid, Version = target.Version, Data = Dto(parent, secondChild) });
        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.Message, "重复");
        Assert.HasCount(0, fixture.Writes);
        Assert.AreEqual(before, fixture.PersistedState());
        Assert.AreEqual(firstChild.Uid, fixture.Rows[target.Uid].ComponentProductId);
        fixture.AssertOwnedTransaction(0, 1);
    }

    /// <summary>软删及硬删最后一行都不能留下父标识有 BOM 的空结构，也不自动清除父产品 BOM 标识。</summary>
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task Delete_RejectsLastCurrentLineForSoftAndHardDelete(bool soft)
    {
        var fixture = new Fixture();
        var parent = fixture.Product("A", "BOM-A");
        var line = fixture.Line(parent, fixture.Product("B"), 1);
        var before = fixture.PersistedState();
        var result = await fixture.Entry.DeleteAsync(new() { Uid = line.Uid, Version = line.Version, IsSoftDelete = soft });
        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.Message, "至少需要保留一条");
        Assert.HasCount(0, fixture.Writes);
        Assert.AreEqual(before, fixture.PersistedState());
        Assert.AreEqual("BOM-A", parent.BomNo);
        Assert.AreEqual(1, parent.BomVersion);
        Assert.IsTrue(fixture.GraphReads > 0);
        fixture.AssertOwnedTransaction(0, 1);
    }

    /// <summary>合法删除须经最终图仍留当前组成，保留历史行；软删与硬删仅按请求改变目标行。</summary>
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task Delete_WhenCurrentLineRemainsUsesKernelAndKeepsHistory(bool soft)
    {
        var fixture = new Fixture();
        var parent = fixture.Product("A", "BOM-A");
        parent.BomVersion = 2;
        fixture.Product("STANDARD-REFERENCE", "BOM-A");
        var child = fixture.Product("B");
        var target = fixture.Line(parent, child, 1);
        var retained = fixture.Line(parent, fixture.Product("C"), 2);
        var history = fixture.Line(parent, child, 1);
        history.BomNo = "ARCHIVE";
        history.BomVersion = 2;
        var result = await fixture.Entry.DeleteAsync(new()
        { Uid = target.Uid, Version = target.Version, IsSoftDelete = soft, ClientKey = "delete-key" });
        Assert.IsTrue(result.Success, result.Message);
        var write = fixture.Writes.Single();
        Assert.AreEqual("DeleteAsync", write.Operation);
        Assert.AreEqual(target.Uid, write.Row.Uid);
        Assert.AreEqual(soft, write.Soft);
        Assert.AreEqual(soft, fixture.Rows.ContainsKey(target.Uid));
        if (soft) Assert.IsTrue(fixture.Rows[target.Uid].IsDeleted);
        Assert.IsFalse(fixture.Rows[retained.Uid].IsDeleted);
        Assert.IsFalse(fixture.Rows[history.Uid].IsDeleted);
        Assert.AreEqual(2, fixture.Rows[history.Uid].BomVersion);
        Assert.AreEqual("ARCHIVE", fixture.Rows[history.Uid].BomNo);
        Assert.AreEqual("BOM-A", parent.BomNo);
        Assert.IsTrue(fixture.GraphReads > 0);
        fixture.AssertOwnedTransaction(1, 0);
    }

    /// <summary>按 BOM 号删除须在事务内解析当前组成并经最终图校验，不能调用旧仓储全版本直接删除。</summary>
    [TestMethod]
    public async Task DeleteByBomNo_CannotBypassEmptyStructureOrDeleteHistory()
    {
        var fixture = new Fixture();
        var parent = fixture.Product("A", "BOM-A");
        parent.BomVersion = 2;
        fixture.Product("STANDARD-REFERENCE", "BOM-A");
        var child = fixture.Product("B");
        fixture.Line(parent, child, 1);
        var history = fixture.Line(parent, child, 1);
        history.BomNo = "ARCHIVE";
        history.BomVersion = 2;
        var before = fixture.PersistedState();
        var result = await fixture.Service.DeleteBomByBomNoAsync("BOM-A");
        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.Message, "至少需要保留一条");
        Assert.HasCount(0, fixture.Writes);
        Assert.AreEqual(before, fixture.PersistedState());
        Assert.IsFalse(fixture.Rows[history.Uid].IsDeleted);
        Assert.AreEqual(2, parent.BomVersion);
        Assert.AreEqual("BOM-A", parent.BomNo);
        Assert.IsTrue(fixture.GraphReads > 0);
        fixture.AssertOwnedTransaction(0, 1);
    }

    /// <summary>已有外层事务时四个保存入口拒绝接管，不读写、不开始新事务、不提交回滚或释放外层事务。</summary>
    [TestMethod]
    [DataRow("add")]
    [DataRow("update")]
    [DataRow("delete")]
    [DataRow("by-no")]
    public async Task ExistingOuterTransaction_IsNeverTakenOver(string entry)
    {
        var fixture = new Fixture();
        var parent = fixture.Product("A", "BOM-A");
        var child = fixture.Product("B");
        var line = fixture.Line(parent, child, 1);
        var outer = fixture.Session.Connection.BeginTransaction();
        fixture.Session.Transaction = outer;
        var success = entry switch
        {
            "add" => (await fixture.Entry.AddAsync(new() { Data = Dto(parent, child) })).Success,
            "update" => (await fixture.Entry.UpdateAsync(new() { Uid = line.Uid, Version = line.Version, Data = Dto(parent, child) })).Success,
            "delete" => (await fixture.Entry.DeleteAsync(new() { Uid = line.Uid, Version = line.Version })).Success,
            _ => (await fixture.Service.DeleteBomByBomNoAsync(parent.BomNo!)).Success
        };
        Assert.IsFalse(success);
        Assert.AreSame(outer, fixture.Session.Transaction);
        Assert.AreEqual(1, fixture.Session.ConnectionStub.Begins);
        Assert.AreEqual(0, fixture.Session.ConnectionStub.Last!.Commits);
        Assert.AreEqual(0, fixture.Session.ConnectionStub.Last.Rollbacks);
        Assert.AreEqual(0, fixture.Session.ConnectionStub.Last.Disposals);
        Assert.AreEqual(0, fixture.RepositoryCalls);
        Assert.HasCount(0, fixture.Writes);
        outer.Rollback();
        outer.Dispose();
        fixture.Session.Transaction = null;
    }

    /// <summary>仅生成输入 DTO，不在测试中预先校验或计算最终图，避免绕开真实服务。</summary>
    private static BomDto Dto(Product parent, Product child) => new()
    { BomNo = parent.BomNo!, SubSkuId = child.Uid, Numerator = 1, Denominator = 1 };

    /// <summary>保持仓储实体读取如 Dapper 独立对象，覆盖所有 BOM 字段用于准备、提交及回滚快照。</summary>
    private static Bom Clone(Bom row) => new()
    {
        Uid = row.Uid, Version = row.Version, IsDeleted = row.IsDeleted, CreatedDate = row.CreatedDate, CreatedBy = row.CreatedBy,
        UpdatedDate = row.UpdatedDate, UpdatedBy = row.UpdatedBy, BomNo = row.BomNo, BomVersion = row.BomVersion,
        ParentProductId = row.ParentProductId, ParentSkuSnapshot = row.ParentSkuSnapshot, LineNumber = row.LineNumber,
        ComponentProductId = row.ComponentProductId, ComponentSkuSnapshot = row.ComponentSkuSnapshot,
        Numerator = row.Numerator, Denominator = row.Denominator, Note = row.Note
    };

    /// <summary>产品查询也返回全部字段的独立副本，避免服务在内存中改父标识而污染持久夹具。</summary>
    private static Product CloneProduct(Product row) => System.Text.Json.JsonSerializer.Deserialize<Product>(
        System.Text.Json.JsonSerializer.Serialize(row))!;

    /// <summary>场景局部仓储通过代理仅实现保存内核实际调用，任何未声明调用失败；持久数据与查询对象分离。</summary>
    private sealed class Fixture
    {
        /// <summary>指定事实读取后发生版本变化的父项，仅用于验证后续定点锁检测，不模拟数据库并发。</summary>
        public Guid? ChangeParentVersionAfterFacts;
        public readonly Dictionary<Guid, Product> Products = [];
        public readonly Dictionary<Guid, Bom> Rows = [];
        public readonly List<(string Operation, Bom Row, bool Soft)> Writes = [];
        public readonly SessionStub Session;
        public readonly BomService Service;
        public BaseService<Bom, BomDto> Entry => Service;
        public int GraphReads;
        public int RepositoryCalls;

        /// <summary>将两个仓储接到同一个记录会话，实际业务逻辑仍由真实 BomService 执行。</summary>
        public Fixture()
        {
            Session = new(Rows);
            var bom = DispatchProxy.Create<IBomRepository, RepositoryProxy>();
            ((RepositoryProxy)(object)bom).Call = BomCall;
            var product = DispatchProxy.Create<IProductRepository, RepositoryProxy>();
            ((RepositoryProxy)(object)product).Call = ProductCall;
            Service = new(bom, product, new ServiceBaseContext(Session, NullLoggerFactory.Instance));
        }

        /// <summary>新增场景产品，BOM 正数仅表示有效引用，不建立版本分组或发布流程。</summary>
        public Product Product(string sku, string? bomNo = null)
        {
            var row = new Product { Uid = Guid.NewGuid(), Sku = sku, MaterialType = "成品", BomNo = bomNo, BomVersion = bomNo is null ? 0 : 1, SupplyType = "采购" };
            Products.Add(row.Uid, row);
            return row;
        }

        /// <summary>建立标准标记为一的物理组成，调用方可显式设置异常旧事实，不提前执行结构校验。</summary>
        public Bom Line(Product parent, Product child, int number)
        {
            var row = new Bom { Uid = Guid.NewGuid(), BomNo = parent.BomNo!, BomVersion = 1,
                ParentProductId = parent.Uid, ParentSkuSnapshot = parent.Sku, ComponentProductId = child.Uid,
                ComponentSkuSnapshot = child.Sku, LineNumber = number, Numerator = 1, Denominator = 1 };
            Rows.Add(row.Uid, row);
            return row;
        }

        /// <summary>完整持久 BOM 快照用于检查零写入失败，不只比较数量或单个组件。</summary>
        public string PersistedState() => System.Text.Json.JsonSerializer.Serialize(Rows.Values.OrderBy(row => row.Uid).ToArray());

        /// <summary>保存内核必须只拥有一个事务，结束后释放并清空会话；仓储调用均已核对同一事务身份。</summary>
        public void AssertOwnedTransaction(int commits, int rollbacks)
        {
            Assert.AreEqual(1, Session.ConnectionStub.Begins);
            Assert.AreEqual(commits, Session.ConnectionStub.Last!.Commits);
            Assert.AreEqual(IsolationLevel.Serializable, Session.ConnectionStub.Last.IsolationLevel);
            Assert.AreEqual(rollbacks, Session.ConnectionStub.Last.Rollbacks);
            Assert.AreEqual(1, Session.ConnectionStub.Last.Disposals);
            Assert.IsNull(Session.Transaction);
        }

        /// <summary>每次仓储读写必须处于内核事务，阻断 BaseService 在事务外预检或直接写入的旁路。</summary>
        private void Record()
        {
            RepositoryCalls++;
            Assert.IsNotNull(Session.Transaction, "所有保存读取和写入都应在内核事务内。");
            Assert.AreSame(Session.ConnectionStub.Last, Session.Transaction);
        }

        /// <summary>提供产品身份和锁请求读取，不提供产品写方法，防止保存入口自动改父 BOM 标识。</summary>
        private object ProductCall(string name, object?[] args)
        {
            Record();
            switch (name)
            {
                case nameof(IProductRepository.GetByBomNoAsync):
                    var byBomNo = Products.Values.SingleOrDefault(row => !row.IsDeleted && row.BomNo == (string)args[0]!);
                    return Task.FromResult(byBomNo is null ? null : CloneProduct(byBomNo));
                case nameof(IProductRepository.GetByIdAsync):
                    Assert.IsTrue((bool)args[1]!);
                    var byId = Products.GetValueOrDefault((Guid)args[0]!);
                    return Task.FromResult(byId is null ? null : CloneProduct(byId));
                case nameof(IProductRepository.GetByIdListAsync):
                    Assert.IsTrue((bool)args[1]!);
                    var ids = ((IEnumerable<Guid>)args[0]!).ToHashSet();
                    return Task.FromResult<IReadOnlyList<Product>>(Products.Values.Where(row => ids.Contains(row.Uid) && !row.IsDeleted).Select(CloneProduct).ToArray());
                default: throw new InvalidOperationException("未授权产品仓储调用：" + name);
            }
        }

        /// <summary>仿真版本写入与软硬删除，读取一律克隆；未声明的仓储调用立即失败。</summary>
        private object BomCall(string name, object?[] args)
        {
            Record();
            switch (name)
            {
                case nameof(IBomRepository.GetByIdListAsync):
                    Assert.IsTrue((bool)args[1]!);
                    var ids = ((IEnumerable<Guid>)args[0]!).ToHashSet();
                    return Task.FromResult<IReadOnlyList<Bom>>(Rows.Values.Where(row => ids.Contains(row.Uid) && !row.IsDeleted).Select(Clone).ToArray());
                case nameof(IBomRepository.GetSharedSaveFactsAsync):
                    GraphReads++;
                    Assert.IsTrue((bool)args[1]!, "组成保存须以更新锁读取共享事实。");
                    var facts = SharedBomTestFacts.Build(Products.Values, Rows.Values, (IReadOnlyList<string>)args[0]!);
                    // 注入事实读取之后、父项定点锁之前的并发变更；不模拟数据库锁实现。
                    if (ChangeParentVersionAfterFacts.HasValue) Products[ChangeParentVersionAfterFacts.Value].Version++;
                    return Task.FromResult(facts);
                case nameof(IBomRepository.GetByBomNoAndVersionAsync):
                    return Task.FromResult<IReadOnlyList<Bom>>(Rows.Values.Where(row => !row.IsDeleted && row.BomNo == (string)args[0]!
                        && (int)args[1]! > 0 && row.BomVersion > 0).Select(Clone).ToArray());
                case nameof(IBomRepository.GetBomByBomNoAsync):
                    // 按编号及有效引用者选物理行，来源父和父正标记不决定组成归属。
                    return Task.FromResult<IEnumerable<Bom>>(Rows.Values.Where(row => !row.IsDeleted && row.BomNo == (string)args[0]!
                        && Products.Values.Any(parent => !parent.IsDeleted && parent.BomVersion > 0
                            && row.BomNo == parent.BomNo)).Select(Clone).ToArray());
                case nameof(IBomRepository.AddAsync):
                    var added = Clone((Bom)args[0]!);
                    Writes.Add((name, Clone(added), false));
                    Rows.Add(added.Uid, added);
                    return Task.FromResult(RepositoryWriteResult.Ok(added.Uid, null, added.Version));
                case nameof(IBomRepository.UpdateAsync):
                    var updated = Clone((Bom)args[0]!);
                    Writes.Add((name, Clone(updated), false));
                    var oldVersion = Rows[updated.Uid].Version;
                    Assert.AreEqual(oldVersion, updated.Version, "传给仓储的并发版本应来自 wrapper。");
                    updated.Version++;
                    Rows[updated.Uid] = updated;
                    return Task.FromResult(RepositoryWriteResult.Ok(updated.Uid, oldVersion, updated.Version));
                case nameof(IBomRepository.DeleteAsync):
                    var deleted = Rows[(Guid)args[0]!];
                    Assert.AreEqual(deleted.Version, (int)args[1]!);
                    var soft = (bool)args[2]!;
                    Writes.Add((name, Clone(deleted), soft));
                    if (soft) { Rows[deleted.Uid] = Clone(deleted); Rows[deleted.Uid].IsDeleted = true; }
                    else Rows.Remove(deleted.Uid);
                    return Task.FromResult(RepositoryWriteResult.Ok(deleted.Uid, deleted.Version, soft ? deleted.Version : null));
                default: throw new InvalidOperationException("未授权 BOM 仓储调用：" + name);
            }
        }
    }

    /// <summary>代理仅将真实服务的仓储调用转给场景，不内置业务判断。</summary>
    public class RepositoryProxy : DispatchProxy
    {
        public Func<string, object?[], object> Call { get; set; } = null!;
        /// <summary>按实际接口方法和入参转发，不返回默认成功。</summary>
        protected override object? Invoke(MethodInfo? method, object?[]? args) => Call(method!.Name, args!);
    }

    /// <summary>记录事务归属，并让回滚恢复整个持久 BOM 集合，防止只计次数而不验证状态。</summary>
    private sealed class SessionStub(Dictionary<Guid, Bom> rows) : IDbSession, IDatabaseExceptionClassifier
    {
        public ConnectionStub ConnectionStub { get; } = new(rows);
        public IDbConnection Connection => ConnectionStub;
        public IDbTransaction? Transaction { get; set; }
        public IDatabaseExceptionClassifier Errors => this;
        /// <summary>本组不用提供者异常注入，未知错误不能伪装成业务冲突。</summary>
        public DatabaseFailureKind Classify(Exception exception) => DatabaseFailureKind.Unknown;
    }

    /// <summary>只提供事务，不执行 SQL 或真实连接；每次开始记录完整持久集合快照。</summary>
    private sealed class ConnectionStub(Dictionary<Guid, Bom> rows) : IDbConnection
    {
        public int Begins;
        public TransactionStub? Last;
        public bool FailCommitBefore;
        public bool FailCommitAfter;
        /// <summary>建立场景事务并记录唯一归属。</summary>
        public IDbTransaction BeginTransaction() { Begins++; return Last = new(rows, this, FailCommitBefore, FailCommitAfter); }
        /// <summary>隔离级别重载沿用同一记录机制。</summary>
        public IDbTransaction BeginTransaction(IsolationLevel isolationLevel)
        {
            var transaction = BeginTransaction();
            Last!.RequestedIsolation = isolationLevel;
            return transaction;
        }
        [AllowNull] public string ConnectionString { get; set; } = "";
        public int ConnectionTimeout => 0;
        public string Database => "offline-bom";
        public ConnectionState State => ConnectionState.Open;
        public void Open() { }
        public void Close() { }
        public void Dispose() { }
        public void ChangeDatabase(string databaseName) => throw new NotSupportedException();
        public IDbCommand CreateCommand() => throw new NotSupportedException();
    }

    /// <summary>只仿真持久集合回滚与提交释放计数，不声称实现数据库隔离或并发锁。</summary>
    private sealed class TransactionStub(Dictionary<Guid, Bom> rows, IDbConnection connection,
        bool failCommitBefore, bool failCommitAfter) : IDbTransaction
    {
        private readonly Dictionary<Guid, Bom> _snapshot = rows.ToDictionary(pair => pair.Key, pair => Clone(pair.Value));
        public int Commits;
        public int Rollbacks;
        public int Disposals;
        public bool IsCommitted { get; private set; }
        public IDbConnection Connection => connection;
        public IsolationLevel RequestedIsolation { get; set; } = IsolationLevel.ReadCommitted;
        public IsolationLevel IsolationLevel => RequestedIsolation;
        /// <summary>记录提交尝试，明确区分未提交失败与已提交但确认丢失。</summary>
        public void Commit()
        {
            Commits++;
            if (failCommitBefore) throw new InvalidOperationException("受控提交前失败");
            IsCommitted = true;
            if (failCommitAfter) throw new InvalidOperationException("受控提交后确认丢失");
        }
        /// <summary>恢复完整事务快照，保留历史行和全部字段。</summary>
        public void Rollback()
        {
            Rollbacks++;
            if (IsCommitted) return;
            rows.Clear();
            foreach (var pair in _snapshot) rows.Add(pair.Key, Clone(pair.Value));
        }
        /// <summary>记录释放以检查内核没有释放调用方外层事务。</summary>
        public void Dispose() => Disposals++;
    }
}
