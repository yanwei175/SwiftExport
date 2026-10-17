using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SwiftExport.Contracts.DTOs.Errors;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Services.Base;
using SwiftExport.Application.Services.Products.Catalog.Maintenance;
using SwiftExport.Application.Tests.Products;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Entities.Systems.Dictionaries.Data;
using SwiftExport.Core.Interfaces;
using SwiftExport.Core.Interfaces.Data;
using SwiftExport.Core.Interfaces.Inventory.Warehouses;
using SwiftExport.Core.Interfaces.Products.Catalog;
using SwiftExport.Core.Interfaces.Products.Colors;
using SwiftExport.Core.Interfaces.Products.Attributes;
using SwiftExport.Core.Interfaces.Products.ProductMasters;
using SwiftExport.Contracts.DTOs.Products.Attributes;
using Mapster;
using SwiftExport.Core.Interfaces.Suppliers.Management;
using SwiftExport.Core.Interfaces.Systems.Dictionaries.Data;

namespace SwiftExport.Application.Tests.MasterData;

/// <summary>通过真实产品服务验证单项及批量保存的 BOM 边界；局部事务仅验证编排，不代替实库并发验收。</summary>
[TestClass]
public sealed partial class ProductBomSaveBoundaryTests
{
    /// <summary>无 BOM 产品可绑定合法共享组成，或准备全新空编号；只保存产品，不复制或改写组成。</summary>
    [TestMethod]
    [DataRow("add", false)]
    [DataRow("update", false)]
    [DataRow("add", true)]
    [DataRow("update", true)]
    public async Task OrdinaryBinding_ValidSharedOrNewEmptyNumberIsSaved(string entry, bool newEmptyNumber)
    {
        var fixture = new Fixture();
        var target = fixture.Product("TARGET");
        var number = newEmptyNumber ? "NEW-EMPTY" : "SHARED";
        if (!newEmptyNumber)
        {
            var source = fixture.Product("SOURCE", number, 5);
            fixture.Product("OTHER-POSITIVE", number, 8);
            fixture.Line(source, fixture.Product("LEAF")).BomVersion = 6;
        }
        var dto = Dto(target);
        dto.Sku = entry == "add" ? "CREATED" : target.Sku;
        dto.BomNo = number;
        dto.BomVersion = 3;
        dto.MaterialType = MaterialTypesEnum.成品;
        var bomBefore = fixture.BomState();
        var result = entry == "add"
            ? await fixture.Entry.AddAsync(new() { Data = dto })
            : await fixture.Entry.UpdateAsync(new() { Uid = target.Uid, Version = target.Version, Data = dto });
        Assert.IsTrue(result.Success, result.Message);
        Assert.HasCount(1, fixture.Writes);
        var saved = fixture.Products[result.Data.Uid!.Value];
        Assert.AreEqual(number, saved.BomNo);
        Assert.AreEqual(3, saved.BomVersion);
        Assert.AreEqual(bomBefore, fixture.BomState());
        Assert.IsTrue(fixture.ReferenceReads > 0);
        fixture.AssertTransaction(1, 0);
        fixture.AssertNoBomBypass();
    }

    /// <summary>普通绑定须检查新增共享父展开后的自环、跨组循环、父资格及孤立历史，危险绑定在写入前拒绝。</summary>
    [TestMethod]
    [DataRow("self", "不能相同")]
    [DataRow("cycle", "循环依赖")]
    [DataRow("customer-parent", "客供件")]
    [DataRow("orphan", "孤立历史")]
    [DataRow("abnormal", "异常组成有效标记")]
    [DataRow("nonpositive", "异常组成有效标记")]
    [DataRow("existing-empty", "没有有效组成")]
    public async Task OrdinaryBinding_DangerousSharedGraphIsRejected(string scenario, string expectedMessage)
    {
        var fixture = new Fixture();
        var target = fixture.Product("TARGET");
        var source = fixture.Product("SOURCE", "SHARED");
        var child = fixture.Product("LEAF");
        if (scenario == "self") child = target;
        if (scenario == "cycle")
        {
            child.MaterialType = MaterialTypesEnum.成品;
            child.BomNo = "DOWNSTREAM";
            child.BomVersion = 1;
            fixture.Line(child, target);
        }
        if (scenario != "existing-empty")
        {
            var line = fixture.Line(source, child);
            if (scenario == "abnormal") line.BomVersion = 0;
            if (scenario == "nonpositive") line.BomVersion = -2;
        }
        if (scenario == "orphan") source.IsDeleted = true;
        var dto = Dto(target);
        dto.BomNo = "SHARED";
        dto.BomVersion = 1;
        dto.MaterialType = MaterialTypesEnum.成品;
        if (scenario == "customer-parent") dto.MaterialType = MaterialTypesEnum.客供件;
        var before = fixture.State();
        var result = await fixture.Entry.UpdateAsync(new() { Uid = target.Uid, Version = target.Version, Data = dto });
        StringAssert.Contains(result.Message, expectedMessage);
        fixture.AssertBlocked(result.Success, before);
    }

    /// <summary>编号共享不依赖物理行来源父，非来源父删除或客供角色变更同样受到结构保护。</summary>
    [TestMethod]
    [DataRow("delete")]
    [DataRow("customer-role")]
    public async Task NonSourceSharedParent_IsProtected(string entry)
    {
        var fixture = new Fixture();
        var source = fixture.Product("SOURCE", "SHARED");
        var other = fixture.Product("OTHER", "SHARED", 7);
        fixture.Line(source, fixture.Product("LEAF"));
        var dto = Dto(other);
        dto.MaterialType = MaterialTypesEnum.客供件;
        var before = fixture.State();
        var result = entry == "delete"
            ? await fixture.Entry.DeleteAsync(new() { Uid = other.Uid, Version = other.Version })
            : await fixture.Entry.UpdateAsync(new() { Uid = other.Uid, Version = other.Version, Data = dto });
        fixture.AssertBlocked(result.Success, before);
    }

    /// <summary>非空共享组全部引用父均可使用不同正标记，修改不复制组成、不改变快照或旧行标记。</summary>
    [TestMethod]
    public async Task ParentPositiveMarkerChange_IsSavedWithoutChangingSharedLines()
    {
        var fixture = new Fixture();
        var parent = fixture.Product("P", "SHARED");
        fixture.Product("OTHER", "SHARED", 7);
        fixture.Line(parent, fixture.Product("LEAF")).BomVersion = 8;
        var dto = Dto(parent);
        dto.BomVersion = 3;
        var beforeBom = fixture.BomState();
        var result = await fixture.Entry.UpdateAsync(new() { Uid = parent.Uid, Version = parent.Version, Data = dto });
        Assert.IsTrue(result.Success, result.Message);
        Assert.AreEqual(3, fixture.Products[parent.Uid].BomVersion);
        Assert.AreEqual(beforeBom, fixture.BomState());
        Assert.HasCount(1, fixture.Writes);
        fixture.AssertTransaction(1, 0);
    }

    /// <summary>旧基础服务类型调用也必须进入批量事务，保持客户端定位、服务端身份及保存版本回执。</summary>
    [TestMethod]
    [DataRow("add")]
    [DataRow("update")]
    [DataRow("soft-delete")]
    [DataRow("hard-delete")]
    public async Task SingleEntry_UsesOwnedTransactionAndReturnsReceipt(string entry)
    {
        var fixture = new Fixture();
        var product = fixture.Product("OLD");
        var input = Dto(product);
        input.Uid = Guid.NewGuid();
        input.Version = 999;
        input.Note = "本次修改";
        var result = entry switch
        {
            "add" => await fixture.Entry.AddAsync(new() { Data = new() { Uid = product.Uid, IsDeleted = true,
                Sku = "NEW", MaterialType = MaterialTypesEnum.零件, SupplyType = SupplyTypeEnums.采购 }, ClientKey = "entry", RowIndex = 8 }),
            "update" => await fixture.Entry.UpdateAsync(new() { Uid = product.Uid, Version = product.Version,
                Data = input, ClientKey = "entry", RowIndex = 8 }),
            _ => await fixture.Entry.DeleteAsync(new() { Uid = product.Uid, Version = product.Version,
                IsSoftDelete = entry == "soft-delete", ClientKey = "entry", RowIndex = 8 })
        };
        Assert.IsTrue(result.Success, result.Message);
        Assert.IsTrue(result.Data.Success);
        Assert.AreEqual("entry", result.Data.ClientKey);
        Assert.AreEqual(8, result.Data.RowIndex);
        Assert.AreEqual(1, fixture.Writes.Count);
        if (entry == "add")
        {
            Assert.AreEqual(SaveOperation.Create, result.Data.Operation);
            Assert.IsNotNull(result.Data.Uid);
            Assert.AreNotEqual(Guid.Empty, result.Data.Uid.Value);
            Assert.AreNotEqual(product.Uid, result.Data.Uid.Value, "新增必须生成新身份，不能复用 DTO 中已有产品的身份。");
            Assert.IsNull(result.Data.OldVersion);
            Assert.AreEqual(1, result.Data.NewVersion);
            Assert.AreEqual("NEW", fixture.Products[result.Data.Uid.Value].Sku);
            Assert.IsFalse(fixture.Products[result.Data.Uid.Value].IsDeleted, "新增必须清除 DTO 中的删除标记。");
            Assert.AreEqual(2, fixture.Products.Count);
            Assert.AreEqual("OLD", fixture.Products[product.Uid].Sku);
            Assert.AreEqual(5, fixture.Products[product.Uid].Version);
            Assert.IsFalse(fixture.Products[product.Uid].IsDeleted);
        }
        else
        {
            Assert.AreEqual(product.Uid, result.Data.Uid);
            Assert.AreEqual(5, result.Data.OldVersion);
            Assert.AreEqual(entry == "update" ? SaveOperation.Update : SaveOperation.Delete, result.Data.Operation);
            if (entry == "update")
            {
                Assert.AreEqual(6, result.Data.NewVersion);
                Assert.AreEqual(product.Uid, fixture.Writes.Single().Row.Uid);
                Assert.AreEqual(5, fixture.Writes.Single().Row.Version);
                Assert.AreEqual("本次修改", fixture.Products[product.Uid].Note);
            }
            else if (entry == "soft-delete")
            {
                Assert.AreEqual(5, result.Data.NewVersion, "软删除回执保留原版本，与真实泛型仓储一致。");
                Assert.IsTrue(fixture.Products[product.Uid].IsDeleted);
                Assert.AreEqual(5, fixture.Products[product.Uid].Version);
                Assert.AreEqual(product.Sku, fixture.Products[product.Uid].Sku);
                Assert.AreEqual(product.Note, fixture.Products[product.Uid].Note);
            }
            else
            {
                Assert.IsNull(result.Data.NewVersion);
                Assert.IsFalse(fixture.Products.ContainsKey(product.Uid));
            }
        }
        fixture.AssertTransaction(1, 0);
        fixture.AssertNoBomBypass();
    }

    /// <summary>并发依据来自 wrapper；DTO 的伪造版本不能把过期修改或删除变为成功。</summary>
    [TestMethod]
    [DataRow("update")]
    [DataRow("delete")]
    public async Task StaleWrapperVersion_RollsBackWithoutChangingPersistentState(string entry)
    {
        var fixture = new Fixture();
        var product = fixture.Product("OLD");
        var dto = Dto(product);
        dto.Version = product.Version;
        dto.Uid = Guid.NewGuid();
        dto.Note = "不应持久化";
        var before = fixture.State();
        var result = entry == "update"
            ? await fixture.Entry.UpdateAsync(new() { Uid = product.Uid, Version = 4, Data = dto })
            : await fixture.Entry.DeleteAsync(new() { Uid = product.Uid, Version = 4 });
        Assert.IsFalse(result.Success);
        Assert.AreEqual(before, fixture.State());
        Assert.AreEqual(0, fixture.SuccessfulWrites);
        Assert.AreEqual(ServiceErrorCodes.BusinessBlocked, result.Error.Code, "当前产品版本冲突沿用整批业务阻断契约。");
        Assert.IsNull(result.Data, "失败不能回传看似可接纳的行级成功结果。");
        fixture.AssertTransaction(0, 1);
        fixture.AssertNoBomBypass();
    }

    /// <summary>普通产品编辑不能移走当前共享编号或关闭有效标记，也不能借编号改动激活历史组成。</summary>
    [TestMethod]
    [DataRow("current-no")]
    [DataRow("current-version")]
    [DataRow("activate-history")]
    [DataRow("leading-space-trim")]
    public async Task ParentBomIdentityChange_IsRejectedBeforeWrite(string scenario)
    {
        var fixture = new Fixture();
        var parent = fixture.Product("P", "BOM-P", 2);
        var child = fixture.Product("C");
        var line = fixture.Line(parent, child);
        var dto = Dto(parent);
        if (scenario == "current-no") dto.BomNo = "OTHER";
        if (scenario == "current-version") dto.BomVersion = 0;
        if (scenario == "leading-space-trim")
        {
            // 存储编号的前导空格有意义，普通 DTO 的 Trim 不能静默移走原当前组成。
            parent.BomNo = " BOM-P";
            line.BomNo = " BOM-P";
            dto.BomNo = " BOM-P";
        }
        if (scenario == "activate-history")
        {
            line.BomNo = "HISTORY";
            line.BomVersion = 1;
            dto.BomNo = "HISTORY";
            dto.BomVersion = 1;
        }
        var before = fixture.State();
        var result = await fixture.Entry.UpdateAsync(new() { Uid = parent.Uid, Version = parent.Version, Data = dto });
        fixture.AssertBlocked(result.Success, before);
        if (scenario == "current-version")
            Assert.AreEqual(0, fixture.ReferenceReads, "编号和标记不成对时应在共享图查询前拒绝。");
        else Assert.IsTrue(fixture.ReferenceReads > 0);
    }

    /// <summary>有效父项和组件的软硬删除都必须阻断，数据库确认的全半角匹配事实也不能被 C# 过滤丢弃。</summary>
    [TestMethod]
    [DataRow("parent", true)]
    [DataRow("parent", false)]
    [DataRow("child", true)]
    [DataRow("child", false)]
    [DataRow("parent-fullwidth", true)]
    [DataRow("child-fullwidth", true)]
    public async Task Delete_CurrentReferenceIsRejected(string role, bool soft)
    {
        var fixture = new Fixture();
        var parent = fixture.Product("P", "BOM-P");
        var child = fixture.Product("C");
        var line = fixture.Line(parent, child);
        if (role.EndsWith("-fullwidth", StringComparison.Ordinal))
        {
            line.BomNo = "ＢＯＭ－Ｐ";
            // 由夹具直接给出数据库已匹配事实，不用 C# 比较冒充 ICU 全半角等值。
            fixture.DatabaseMatchedCurrentLineIds.Add(line.Uid);
        }
        var target = role.StartsWith("parent", StringComparison.Ordinal) ? parent : child;
        var before = fixture.State();
        var result = await fixture.Entry.DeleteAsync(new() { Uid = target.Uid, Version = target.Version, IsSoftDelete = soft });
        fixture.AssertBlocked(result.Success, before);
        Assert.IsTrue(fixture.ReferenceReads > 0);
    }

    /// <summary>更新 DTO 携带删除标记不能绕过删除入口的引用约束。</summary>
    [TestMethod]
    [DataRow("parent")]
    [DataRow("child")]
    public async Task Update_IsDeletedCannotBypassCurrentReferenceGuard(string role)
    {
        var fixture = new Fixture();
        var parent = fixture.Product("P", "BOM-P");
        var child = fixture.Product("C");
        fixture.Line(parent, child);
        var target = role == "parent" ? parent : child;
        var dto = Dto(target);
        dto.IsDeleted = true;
        var before = fixture.State();
        var result = await fixture.Entry.UpdateAsync(new() { Uid = target.Uid, Version = target.Version, Data = dto });
        fixture.AssertBlocked(result.Success, before);
    }

    /// <summary>角色变更必须维持受影响结构合法；非法父客供、非采购客供子件及拥有 BOM 的客供子件均拒绝。</summary>
    [TestMethod]
    [DataRow("parent-customer")]
    [DataRow("child-produced-customer")]
    [DataRow("child-with-bom")]
    public async Task RoleChange_InvalidAffectedStructureIsRejected(string scenario)
    {
        var fixture = new Fixture();
        var parent = fixture.Product("P", "BOM-P");
        var child = fixture.Product("C");
        fixture.Line(parent, child);
        var target = scenario == "parent-customer" ? parent : child;
        if (scenario == "child-with-bom")
        {
            child.BomNo = "BOM-C";
            child.BomVersion = 1;
            fixture.Line(child, fixture.Product("G"));
        }
        var dto = Dto(target);
        dto.MaterialType = MaterialTypesEnum.客供件;
        dto.SupplyType = scenario == "child-produced-customer" ? SupplyTypeEnums.生产 : SupplyTypeEnums.采购;
        var before = fixture.State();
        var result = await fixture.Entry.UpdateAsync(new() { Uid = target.Uid, Version = target.Version, Data = dto });
        fixture.AssertBlocked(result.Success, before);
    }

    /// <summary>合法的采购终端子件改为客供件可以保存；不得批量改写 BOM 的角色或历史快照。</summary>
    [TestMethod]
    public async Task RoleChange_ValidCustomerSuppliedLeafIsSaved()
    {
        var fixture = new Fixture();
        var parent = fixture.Product("P", "BOM-P");
        var child = fixture.Product("C");
        fixture.Line(parent, child);
        var dto = Dto(child);
        dto.MaterialType = MaterialTypesEnum.客供件;
        var bomBefore = fixture.BomState();
        var result = await fixture.Entry.UpdateAsync(new() { Uid = child.Uid, Version = child.Version, Data = dto });
        Assert.IsTrue(result.Success, result.Message);
        Assert.AreEqual(MaterialTypesEnum.客供件, fixture.Products[child.Uid].MaterialType);
        Assert.AreEqual(bomBefore, fixture.BomState());
        fixture.AssertTransaction(1, 0);
        fixture.AssertNoBomBypass();
    }

    /// <summary>当前引用和历史快照不冻结无关资料修改，SKU、描述与备注保存后组成快照仍保持原值。</summary>
    [TestMethod]
    [DataRow("parent", true)]
    [DataRow("child", true)]
    [DataRow("parent", false)]
    [DataRow("child", false)]
    public async Task OrdinaryEdit_PreservesCurrentAndHistoricalBomSnapshots(string role, bool hasCurrentLine)
    {
        var fixture = new Fixture();
        var parent = fixture.Product("P", "BOM-P", 2);
        var child = fixture.Product("C");
        if (hasCurrentLine) fixture.Line(parent, child);
        var history = fixture.Line(parent, child);
        history.BomNo = "ARCHIVE";
        history.BomVersion = 2;
        history.ParentSkuSnapshot = "历史父编码";
        history.ComponentSkuSnapshot = "历史子编码";
        var target = role == "parent" ? parent : child;
        var dto = Dto(target);
        dto.Sku += "-RENAMED";
        dto.DescriptionZh = "正常资料修改";
        dto.Note = "本次备注";
        var beforeBom = fixture.BomState();
        var result = await fixture.Entry.UpdateAsync(new() { Uid = target.Uid, Version = target.Version, Data = dto });
        Assert.IsTrue(result.Success, result.Message);
        Assert.AreEqual(dto.Sku, fixture.Products[target.Uid].Sku);
        Assert.AreEqual(dto.DescriptionZh, fixture.Products[target.Uid].DescriptionZh);
        Assert.AreEqual(dto.Note, fixture.Products[target.Uid].Note);
        Assert.AreEqual(beforeBom, fixture.BomState());
        Assert.AreEqual(0, fixture.ReferenceReads, "SKU、描述与备注不变更结构字段时应跳过共享事实查询。");
        fixture.AssertTransaction(1, 0);
        fixture.AssertNoBomBypass();
    }

    /// <summary>纯历史组成、已删组成及已删父记录下的组成不构成当前有效引用；删除不修改历史行。</summary>
    [TestMethod]
    [DataRow("history")]
    [DataRow("deleted-line")]
    [DataRow("deleted-parent")]
    public async Task Delete_NonCurrentReferenceDoesNotFreezeProduct(string scenario)
    {
        var fixture = new Fixture();
        var parent = fixture.Product("P", "BOM-P", 2);
        var child = fixture.Product("C");
        var line = fixture.Line(parent, child);
        if (scenario == "history") { line.BomNo = "ARCHIVE"; line.BomVersion = 2; }
        if (scenario == "deleted-line") line.IsDeleted = true;
        if (scenario == "deleted-parent") parent.IsDeleted = true;
        var beforeBom = fixture.BomState();
        var result = await fixture.Entry.DeleteAsync(new() { Uid = child.Uid, Version = child.Version });
        Assert.IsTrue(result.Success, result.Message);
        Assert.IsTrue(fixture.Products[child.Uid].IsDeleted);
        Assert.AreEqual(beforeBom, fixture.BomState());
        fixture.AssertTransaction(1, 0);
        fixture.AssertNoBomBypass();
    }

    /// <summary>批量后序写入冲突必须恢复前序新增，保留全部产品与历史 BOM 持久状态。</summary>
    [TestMethod]
    public async Task BatchWriteFailure_RestoresAllPersistentState()
    {
        var fixture = new Fixture { FailWriteNumber = 2 };
        var existing = fixture.Product("OLD");
        var historyParent = fixture.Product("P", "BOM-P", 2);
        fixture.Line(historyParent, existing).BomVersion = 1;
        var input = Dto(existing);
        input.Note = "不应保存";
        var before = fixture.State();
        var result = await fixture.Service.BatchSaveAsync(new()
        {
            CreateItems = [new() { Data = new() { Sku = "NEW", MaterialType = MaterialTypesEnum.零件, SupplyType = SupplyTypeEnums.采购 } }],
            UpdateItems = [new() { Uid = existing.Uid, Version = existing.Version, Data = input }]
        });
        Assert.IsFalse(result.Success);
        Assert.AreEqual(2, fixture.Writes.Count);
        Assert.AreEqual(1, fixture.SuccessfulWrites);
        Assert.AreEqual(before, fixture.State());
        fixture.AssertTransaction(0, 1);
        fixture.AssertNoBomBypass();
    }

    /// <summary>单项和批量保存均不得提交、回滚、释放外层事务，也不得先行访问仓储。</summary>
    [TestMethod]
    [DataRow("add")]
    [DataRow("update")]
    [DataRow("delete")]
    [DataRow("batch")]
    public async Task ExistingOuterTransaction_IsNotTakenOver(string entry)
    {
        var fixture = new Fixture();
        var product = fixture.Product("OLD");
        var outer = fixture.Session.Connection.BeginTransaction();
        fixture.Session.Transaction = outer;
        var before = fixture.State();
        var success = entry switch
        {
            "add" => (await fixture.Entry.AddAsync(new() { Data = Dto(product) })).Success,
            "update" => (await fixture.Entry.UpdateAsync(new() { Uid = product.Uid, Version = product.Version, Data = Dto(product) })).Success,
            "delete" => (await fixture.Entry.DeleteAsync(new() { Uid = product.Uid, Version = product.Version })).Success,
            _ => (await fixture.Service.BatchSaveAsync(new() { UpdateItems = [new() { Uid = product.Uid, Version = product.Version, Data = Dto(product) }] })).Success
        };
        Assert.IsFalse(success);
        Assert.AreSame(outer, fixture.Session.Transaction);
        Assert.AreEqual(1, fixture.Session.ConnectionStub.Begins);
        Assert.AreEqual(0, fixture.Session.ConnectionStub.Last!.Commits);
        Assert.AreEqual(0, fixture.Session.ConnectionStub.Last.Rollbacks);
        Assert.AreEqual(0, fixture.Session.ConnectionStub.Last.Disposals);
        Assert.AreEqual(0, fixture.RepositoryCalls);
        Assert.AreEqual(before, fixture.State());
        outer.Rollback();
        outer.Dispose();
        fixture.Session.Transaction = null;
    }

    /// <summary>从产品资料生成输入，不提前执行被测校验；所有测试都调用真实服务。</summary>
    private static ProductDto Dto(Product product) => new()
    {
        Uid = product.Uid, Version = product.Version, Sku = product.Sku, MaterialType = product.MaterialType!,
        ProductMasterId = product.ProductMasterId, LinkSku = product.LinkSku,
        SupplyType = product.SupplyType, BomNo = product.BomNo!, BomVersion = product.BomVersion,
        Unit = product.Unit!, Note = product.Note!, DescriptionZh = product.DescriptionZh!, IsDeleted = product.IsDeleted
    };

    /// <summary>明确序列化终止跨读取、写入和提交阶段返回服务不可用，不自动重复任何产品写入。</summary>
    [TestMethod]
    [DataRow("read", 0, 0, false)]
    [DataRow("write", 2, 0, false)]
    [DataRow("commit", 2, 1, false)]
    [DataRow("read", 0, 0, true)]
    [DataRow("write", 2, 0, true)]
    [DataRow("commit", 2, 1, true)]
    public async Task SerializationFailure_ReturnsUnavailableAndRestoresBatch(string stage, int writes, int commits, bool cleanupFaults)
    {
        var fixture = new Fixture { SerializationStage = stage, FailLogging = cleanupFaults };
        fixture.Session.ConnectionStub.SerializationOnCommit = stage == "commit";
        fixture.Session.ConnectionStub.FailRollback = cleanupFaults;
        fixture.Session.ConnectionStub.FailTransactionDispose = cleanupFaults;
        fixture.Session.ConnectionStub.FailDispose = cleanupFaults;
        var target = fixture.Product("OLD");
        var dto = Dto(target);
        dto.Note = "批次修改";
        var create = new ProductDto { Sku = "NEW", SupplyType = SupplyTypeEnums.采购, MaterialType = MaterialTypesEnum.零件 };
        if (stage == "read") { create.MaterialType = MaterialTypesEnum.成品; create.BomNo = "NEW-EMPTY"; create.BomVersion = 3; }
        var before = fixture.State();
        var result = await fixture.Service.BatchSaveAsync(new()
        {
            CreateItems = [new() { Data = create }],
            UpdateItems = [new() { Uid = target.Uid, Version = target.Version, Data = dto }]
        });
        Assert.IsFalse(result.Success);
        Assert.AreEqual(ServiceErrorCodes.ServiceUnavailable, result.Error.Code);
        StringAssert.Contains(result.Message, "本次未提交");
        Assert.IsNull(result.Data);
        Assert.AreEqual(writes, fixture.Writes.Count);
        if (!cleanupFaults) Assert.AreEqual(before, fixture.State());
        Assert.IsFalse(fixture.Session.ConnectionStub.Last!.IsCommitted);
        Assert.AreEqual(cleanupFaults ? 1 : 0, fixture.Session.ConnectionStub.Disposals);
        fixture.AssertTransaction(commits, 1);
    }

    /// <summary>未知提交异常区分提交前失败和确认丢失，回滚不得伪造撤销已提交事实，也不能重复写入。</summary>
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(true, true)]
    public async Task CommitUnknownOutcome_PreservesActualFacts(bool committed, bool cleanupFaults)
    {
        var fixture = new Fixture { FailLogging = cleanupFaults };
        fixture.Session.ConnectionStub.FailCommitBefore = !committed;
        fixture.Session.ConnectionStub.FailCommitAfter = committed;
        fixture.Session.ConnectionStub.FailRollback = cleanupFaults;
        fixture.Session.ConnectionStub.FailTransactionDispose = cleanupFaults;
        fixture.Session.ConnectionStub.FailDispose = cleanupFaults;
        var target = fixture.Product("OLD");
        var dto = Dto(target);
        dto.Note = "新事实";
        var before = fixture.State();
        var result = await fixture.Entry.UpdateAsync(new() { Uid = target.Uid, Version = target.Version, Data = dto });
        Assert.IsFalse(result.Success);
        Assert.AreEqual(ServiceErrorCodes.OutcomeUnknown, result.Error.Code);
        Assert.IsNull(result.Data);
        Assert.HasCount(1, fixture.Writes);
        Assert.AreEqual(committed, fixture.Session.ConnectionStub.Last!.IsCommitted);
        if (committed) Assert.AreEqual(dto.Note, fixture.Products[target.Uid].Note);
        else if (!cleanupFaults) Assert.AreEqual(before, fixture.State());
        Assert.AreEqual(cleanupFaults ? 1 : 0, fixture.Session.ConnectionStub.Disposals);
        fixture.AssertTransaction(1, 1);
    }

    /// <summary>创建事务失败且诊断日志失效时，不能释放未取得的事务或连接，也不能开始产品写入。</summary>
    [TestMethod]
    public async Task BeginFailure_DoesNotCleanUnownedResources()
    {
        var fixture = new Fixture { FailLogging = true };
        fixture.Session.ConnectionStub.FailBegin = true;
        var result = await fixture.Entry.AddAsync(new() { Data = new() { Sku = "NEW" } });
        Assert.IsFalse(result.Success);
        Assert.AreEqual(ServiceErrorCodes.Unexpected, result.Error.Code);
        Assert.HasCount(0, fixture.Writes);
        Assert.AreEqual(1, fixture.Session.ConnectionStub.Begins);
        Assert.AreEqual(0, fixture.Session.ConnectionStub.Disposals);
        Assert.IsNull(fixture.Session.ConnectionStub.Last);
        Assert.IsNull(fixture.Session.Transaction);
    }

    /// <summary>成功提交或写入失败遇到释放、回滚及日志故障时保留原始结果并清空会话。</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CleanupAndLoggingFaults_DoNotOverrideResult(bool writeFails)
    {
        var fixture = new Fixture { FailWriteNumber = writeFails ? 1 : 0, FailLogging = true };
        fixture.Session.ConnectionStub.FailRollback = writeFails;
        fixture.Session.ConnectionStub.FailTransactionDispose = true;
        fixture.Session.ConnectionStub.FailDispose = true;
        var target = fixture.Product("OLD");
        var dto = Dto(target);
        dto.Note = "本次保存";
        var result = await fixture.Entry.UpdateAsync(new() { Uid = target.Uid, Version = target.Version, Data = dto });
        Assert.AreEqual(!writeFails, result.Success);
        if (writeFails) Assert.AreEqual(ServiceErrorCodes.BusinessBlocked, result.Error.Code);
        else Assert.AreEqual(dto.Note, fixture.Products[target.Uid].Note);
        Assert.HasCount(1, fixture.Writes);
        Assert.AreEqual(1, fixture.Session.ConnectionStub.Disposals);
        Assert.IsTrue(fixture.LoggerCalls > 0);
        fixture.AssertTransaction(writeFails ? 0 : 1, writeFails ? 1 : 0);
    }

    /// <summary>完整克隆持久对象，使查询对象与已保存状态分离，捕捉失败时的内存污染。</summary>
    private static T Clone<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;

    /// <summary>局部 fake 只提供实际保存所需仓储读写，事务会恢复完整产品及 BOM 快照。</summary>
    private sealed partial class Fixture
    {
        public Dictionary<Guid, Product> Products { get; } = [];
        public Dictionary<Guid, Bom> Boms { get; } = [];
        public Dictionary<Guid, ProductAttributeSelection> Selections { get; } = [];
        /// <summary>包装事实参与同一 fake 事务快照，能检测后序资料或标签失败的原子回滚。</summary>
        public Dictionary<Guid, ProductPackage> Packages { get; } = [];
        public Dictionary<Guid, ProductMaster> Masters { get; } = [];
        public Dictionary<Guid, SysDictData> Profiles { get; } = [];
        public List<ProductAttributeGroupDto> Groups { get; } = [];
        public List<string> DetailLockTrace { get; } = [];
        public bool FailSelectionWrite { get; set; }
        public HashSet<Guid> DatabaseMatchedCurrentLineIds { get; } = [];
        public List<(string Operation, Product Row)> Writes { get; } = [];
        public SessionStub Session { get; }
        public ProductMaintenanceService Service { get; }
        public BaseService<Product, ProductDto> Entry => Service;
        public int RepositoryCalls;
        public int ReferenceReads;
        public int BomBypassCalls;
        public int FailWriteNumber;
        public int SuccessfulWrites;
        public string SerializationStage { get; init; } = string.Empty;
        private readonly FaultLogger _logger = new();
        public bool FailLogging { init => _logger.Fail = value; }
        public int LoggerCalls => _logger.Calls;

        /// <summary>所有仓储共享同一会话；没有引用资料时任何额外颜色、供应商、仓库查询均视为越界。</summary>
        public Fixture()
        {
            Session = new(Products, Boms, Selections, Packages);
            Service = new(Proxy<IProductRepository>(ProductCall), new ServiceBaseContext(Session, new FaultLoggerFactory(_logger)),
                Proxy<IProductColorRepository>(Unexpected), Proxy<ISupplierRepository>(Unexpected),
                Proxy<IWarehouseQueryRepository>(Unexpected), Proxy<IBomRepository>(BomCall),
                Proxy<ISysDictDataRepository>((name, _) =>
                {
                    Record();
                    Assert.AreEqual(nameof(ISysDictDataRepository.GetByCodeAsync), name);
                    return Task.FromResult<IReadOnlyList<SysDictData>>([]);
                }), Proxy<IProductAttributeRepository>(AttributeCall), Proxy<IProductMasterRepository>(MasterCall));
        }

        /// <summary>建立可选当前 BOM 身份；有编号时默认成品，无编号时默认零件，版本五用于区别 wrapper 与 DTO 伪造版本。</summary>
        public Product Product(string sku, string? bomNo = null, int bomVersion = 1)
        {
            var product = new Product { Uid = Guid.NewGuid(), Version = 5, Sku = sku, MaterialType = bomNo is null ? MaterialTypesEnum.零件 : MaterialTypesEnum.成品,
                SupplyType = SupplyTypeEnums.采购, BomNo = bomNo, BomVersion = bomNo is null ? 0 : bomVersion };
            Products.Add(product.Uid, product);
            return product;
        }

        /// <summary>建立持久组成事实，历史、删除及非法场景由测试直接指定，不预先计算有效图。</summary>
        public Bom Line(Product parent, Product child)
        {
            var line = new Bom { Uid = Guid.NewGuid(), Version = 3, ParentProductId = parent.Uid,
                ComponentProductId = child.Uid, BomNo = parent.BomNo!, BomVersion = 1,
                ParentSkuSnapshot = parent.Sku, ComponentSkuSnapshot = child.Sku, Numerator = 1, Denominator = 1,
                LineNumber = Boms.Count + 1, Note = "保留的组成快照" };
            Boms.Add(line.Uid, line);
            return line;
        }

        /// <summary>比较全部产品及 BOM 字段，而非只比较行数或写入计数。</summary>
        public string State() => JsonSerializer.Serialize(new { Products = Products.Values.OrderBy(x => x.Uid), Boms = Boms.Values.OrderBy(x => x.Uid), Packages = Packages.Values.OrderBy(x => x.Uid) });
        /// <summary>独立比较当前与历史组成事实，捕捉普通资料保存顺手改快照。</summary>
        public string BomState() => JsonSerializer.Serialize(Boms.Values.OrderBy(x => x.Uid));

        /// <summary>阻断条件必须零写入，结束内核事务且不遗留任何持久变更。</summary>
        public void AssertBlocked(bool success, string before)
        {
            Assert.IsFalse(success);
            Assert.AreEqual(0, Writes.Count);
            Assert.AreEqual(before, State());
            AssertTransaction(0, 1);
            AssertNoBomBypass();
        }

        /// <summary>核对唯一事务所有权、提交回滚及释放，保存结束必须清空会话。</summary>
        public void AssertTransaction(int commits, int rollbacks)
        {
            Assert.AreEqual(1, Session.ConnectionStub.Begins);
            Assert.AreEqual(commits, Session.ConnectionStub.Last!.Commits);
            Assert.AreEqual(IsolationLevel.Serializable, Session.ConnectionStub.Last.IsolationLevel);
            Assert.AreEqual(rollbacks, Session.ConnectionStub.Last.Rollbacks);
            Assert.AreEqual(1, Session.ConnectionStub.Last.Disposals);
            Assert.IsNull(Session.Transaction);
        }

        /// <summary>即使服务捕获 fake 异常，也不能把全图读取或 BOM 写入隐藏成预期业务阻断。</summary>
        public void AssertNoBomBypass() => Assert.AreEqual(0, BomBypassCalls);

        /// <summary>全部仓储读取和写入必须在同一内核事务内，旧基础入口事务外预检会失败。</summary>
        private void Record()
        {
            RepositoryCalls++;
            Assert.IsNotNull(Session.Transaction);
            Assert.AreSame(Session.ConnectionStub.Last, Session.Transaction);
        }

        /// <summary>当前有效包装关系引用的包材身份，测试显式提供，不模拟包装编辑。</summary>
        public HashSet<Guid> ReferencedPackagingMaterialIds { get; } = [];

        /// <summary>按真实接口调用提供独立产品对象，不臆测服务的私有方法或调用次数。</summary>
        private object ProductCall(string name, object?[] args)
        {
            Record();
            switch (name)
            {
                case nameof(IProductRepository.LockProductRowsAsync):
                    var lockedIds = ((IEnumerable<Guid>)args[0]!).ToHashSet();
                    DetailLockTrace.Add("product");
                    return Task.FromResult<IReadOnlyList<Product>>(Products.Values.Where(row => lockedIds.Contains(row.Uid)).Select(Clone).ToArray());
                case nameof(IProductRepository.GetPackagesAsync):
                    return Task.FromResult<IReadOnlyList<ProductPackageDto>>(Packages.Values.Where(row => row.OwnerProductId == (Guid)args[0]! && !row.IsDeleted)
                        .Select(row => new ProductPackageDto { Uid = row.Uid, Version = row.Version, OwnerProductId = row.OwnerProductId!.Value, Data = row.Adapt<ProductPackageWriteDto>(), IsPackagingMaterialValid = true }).ToArray());
                case nameof(IProductRepository.StagePackageTypeChangesAsync):
                    return Task.FromResult(((IReadOnlyCollection<Guid>)args[1]!).Count);
                case nameof(IProductRepository.AddPackageAsync):
                case nameof(IProductRepository.UpdatePackageAsync):
                    var package = Clone((ProductPackage)args[0]!);
                    var original = package.Version;
                    package.Version = name == nameof(IProductRepository.AddPackageAsync) ? 1 : original + 1;
                    Packages[package.Uid] = package;
                    return Task.FromResult(RepositoryWriteResult.Ok(package.Uid, original, package.Version, "包装已保存。"));
                case nameof(IProductRepository.GetReferencedPackagingMaterialIdsAsync):
                    var packageIds = ((IEnumerable<Guid>)args[0]!).ToHashSet();
                    return Task.FromResult<IReadOnlyList<Guid>>(ReferencedPackagingMaterialIds.Concat(Packages.Values.Where(row => !row.IsDeleted && row.PackagingMaterialId.HasValue)
                        .Select(row => row.PackagingMaterialId!.Value)).Where(packageIds.Contains).Distinct().ToArray());
                case nameof(IProductRepository.GetByIdListAsync):
                    var ids = ((IEnumerable<Guid>)args[0]!).ToHashSet();
                    return Task.FromResult<IReadOnlyList<Product>>(Products.Values.Where(x => ids.Contains(x.Uid) && !x.IsDeleted).Select(Clone).ToArray());
                case nameof(IProductRepository.GetByIdAsync):
                    var product = Products.GetValueOrDefault((Guid)args[0]!);
                    if (args.Length > 1 && args[1] is true) DetailLockTrace.Add("product");
                    return Task.FromResult(product is null || product.IsDeleted ? null : Clone(product));
                case nameof(IProductRepository.GetDetailAsync):
                    var detailProduct = Products[(Guid)args[0]!];
                    var master = detailProduct.ProductMasterId.HasValue ? Masters.GetValueOrDefault(detailProduct.ProductMasterId.Value) : null;
                    return Task.FromResult<ProductDetailDto?>(new ProductDetailDto
                    {
                        Uid = detailProduct.Uid, Version = detailProduct.Version, Data = detailProduct.Adapt<ProductDetailWriteDto>(),
                        ProductMasterId = detailProduct.ProductMasterId, LinkSku = detailProduct.LinkSku,
                        MasterSku = master is { IsDeleted: false } ? master.MasterSku : null,
                        IsActive = detailProduct.IsActive, BomNo = detailProduct.BomNo, BomVersion = detailProduct.BomVersion,
                        ProductAttributeProfileId = master?.ProductAttributeProfileId
                    });
                case nameof(IProductRepository.GetBySkuListAsync):
                    var skus = ((IEnumerable<string>)args[0]!).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    return Task.FromResult<IReadOnlyList<Product>>(Products.Values.Where(x => !x.IsDeleted && skus.Contains(x.Sku)).Select(Clone).ToArray());
                case nameof(IProductRepository.AddAsync):
                case nameof(IProductRepository.UpdateAsync):
                case nameof(IProductRepository.DeleteAsync):
                    return Write(name, args);
                default: throw new InvalidOperationException("未授权产品调用：" + name);
            }
        }

        /// <summary>模拟乐观并发与软硬删除；新增重建身份并清除删除标记，软删除保留版本，由事务回滚恢复全部状态。</summary>
        private object Write(string name, object?[] args)
        {
            var deletion = name == nameof(IProductRepository.DeleteAsync);
            var row = deletion ? Clone(Products[(Guid)args[0]!]) : Clone((Product)args[0]!);
            var version = deletion ? (int)args[1]! : row.Version;
            Writes.Add((name, Clone(row)));
            if (SerializationStage == "write" && Writes.Count == 2) throw new SerializationFailureStub();
            if (Writes.Count == FailWriteNumber)
                return Task.FromResult(RepositoryWriteResult.Fail(row.Uid, version, "WRITE_FAILED", "受控后序写入失败"));
            if (name != nameof(IProductRepository.AddAsync)
                && (!Products.TryGetValue(row.Uid, out var existing) || existing.Version != version))
                return Task.FromResult(RepositoryWriteResult.Fail(row.Uid, version, "CONCURRENCY_CONFLICT", "产品版本冲突"));
            SuccessfulWrites++;
            if (name == nameof(IProductRepository.AddAsync))
            {
                // 与泛型仓储一致，客户端身份不作为新增主键；正版本保留，非正版本初始化为一。
                row.Uid = Guid.NewGuid();
                row.IsDeleted = false;
                if (row.Version <= 0) row.Version = 1;
                Products.Add(row.Uid, row);
                return Task.FromResult(RepositoryWriteResult.Ok(row.Uid, null, row.Version));
            }
            if (deletion && !(bool)args[2]!)
            {
                Products.Remove(row.Uid);
                return Task.FromResult(RepositoryWriteResult.Ok(row.Uid, version, null));
            }
            row.Version = deletion ? version : version + 1;
            if (deletion) row.IsDeleted = true;
            Products[row.Uid] = row;
            return Task.FromResult(RepositoryWriteResult.Ok(row.Uid, version, row.Version));
        }

        /// <summary>共享查询保留全图和显式数据库匹配事实，兼容旧只读接口；所有组成写入均视为越界。</summary>
        private object BomCall(string name, object?[] args)
        {
            Record();
            switch (name)
            {
                case nameof(IBomRepository.GetCurrentReferencesForProductsAsync):
                    ReferenceReads++;
                    var ids = ((IEnumerable<Guid>)args[0]!).ToHashSet();
                    return Task.FromResult<IReadOnlyList<Bom>>(Boms.Values.Where(x => !x.IsDeleted
                        && (x.ParentProductId.HasValue && ids.Contains(x.ParentProductId.Value)
                            || x.ComponentProductId.HasValue && ids.Contains(x.ComponentProductId.Value))
                        && x.ParentProductId.HasValue && Products.TryGetValue(x.ParentProductId.Value, out var parent)
                        && !parent.IsDeleted && x.BomVersion == parent.BomVersion
                        && (DatabaseMatchedCurrentLineIds.Contains(x.Uid)
                            || string.Equals(x.BomNo?.TrimEnd(' '), parent.BomNo?.TrimEnd(' '), StringComparison.OrdinalIgnoreCase)))
                        .Select(Clone).ToArray());
                case nameof(IBomRepository.GetSharedSaveFactsAsync):
                    ReferenceReads++;
                    if (SerializationStage == "read") throw new SerializationFailureStub();
                    Assert.IsFalse((bool)args[1]!, "普通产品边界只读共享图，不能全图申请更新锁。");
                    return Task.FromResult(SharedBomTestFacts.Build(Products.Values, Boms.Values,
                        (IReadOnlyList<string>)args[0]!, line => DatabaseMatchedCurrentLineIds.Contains(line.Uid)
                            && line.ParentProductId.HasValue && Products.TryGetValue(line.ParentProductId.Value, out var matched)
                                ? matched.BomNo! : line.BomNo));
                case nameof(IBomRepository.GetByBomNoAndVersionAsync):
                    var bomNo = (string)args[0]!;
                    var bomVersion = (int)args[1]!;
                    return Task.FromResult<IReadOnlyList<Bom>>(Boms.Values.Where(x => !x.IsDeleted && bomVersion > 0 && x.BomVersion > 0
                        && string.Equals(x.BomNo?.TrimEnd(' '), bomNo.TrimEnd(' '), StringComparison.OrdinalIgnoreCase))
                        .Select(Clone).ToArray());
                case nameof(IBomRepository.HasActiveBomForProductAsync):
                    var uid = (Guid)args[0]!;
                    var no = (string?)args[1];
                    return Task.FromResult(Boms.Values.Any(x => !x.IsDeleted
                        && (uid != Guid.Empty && x.ParentProductId == uid || !string.IsNullOrWhiteSpace(no) && x.BomNo == no)));
                default:
                    BomBypassCalls++;
                    throw new InvalidOperationException("产品保存不得调用未配置 BOM 接口或写入组成：" + name);
            }
        }

        /// <summary>没有引用资料的场景禁止额外仓储调用，避免 fake 自动返回成功掩盖范围扩大。</summary>
        private object Unexpected(string name, object?[] args)
        {
            Record();
            throw new InvalidOperationException("未配置引用资料调用：" + name);
        }
    }

    /// <summary>建立局部接口代理，不替代产品服务的业务校验。</summary>
    private static T Proxy<T>(Func<string, object?[], object> call) where T : class
    {
        var proxy = DispatchProxy.Create<T, RepositoryProxy>();
        ((RepositoryProxy)(object)proxy).Call = call;
        return proxy;
    }

    /// <summary>转发真实仓储调用；未配置操作必须失败。</summary>
    public class RepositoryProxy : DispatchProxy
    {
        public Func<string, object?[], object> Call { get; set; } = null!;
        /// <summary>保留接口方法及入参传给当前场景。</summary>
        protected override object? Invoke(MethodInfo? method, object?[]? args) => Call(method!.Name, args ?? []);
    }

    /// <summary>共享请求会话，仅用于记录事务，不提供数据库或异常重试。</summary>
    private sealed class SessionStub(Dictionary<Guid, Product> products, Dictionary<Guid, Bom> boms,
        Dictionary<Guid, ProductAttributeSelection> selections, Dictionary<Guid, ProductPackage> packages) : IDbSession, IDatabaseExceptionClassifier
    {
        public ConnectionStub ConnectionStub { get; } = new(products, boms, selections, packages);
        public IDbConnection Connection => ConnectionStub;
        public IDbTransaction? Transaction { get; set; }
        public IDatabaseExceptionClassifier Errors => this;
        /// <summary>只识别受控序列化终止，不按消息或其他异常猜测可重试性。</summary>
        public DatabaseFailureKind Classify(Exception exception) => exception is SerializationFailureStub
            ? DatabaseFailureKind.SerializationFailure : DatabaseFailureKind.Unknown;
    }

    /// <summary>只提供记录型事务，禁止执行 SQL；每次开始捕获全部持久事实。</summary>
    private sealed class ConnectionStub(Dictionary<Guid, Product> products, Dictionary<Guid, Bom> boms,
        Dictionary<Guid, ProductAttributeSelection> selections, Dictionary<Guid, ProductPackage> packages) : IDbConnection
    {
        public int Begins;
        public TransactionStub? Last;
        public bool FailBegin;
        public bool SerializationOnCommit;
        public bool FailCommitBefore;
        public bool FailCommitAfter;
        public bool FailRollback;
        public bool FailTransactionDispose;
        public bool FailDispose;
        public int Disposals;
        /// <summary>建立产品保存拥有的唯一事务。</summary>
        public IDbTransaction BeginTransaction()
        {
            Begins++;
            if (FailBegin) throw new InvalidOperationException("受控事务创建失败");
            return Last = new(products, boms, selections, packages, this);
        }
        /// <summary>显式隔离重载仍只记录生命周期，不声称验证数据库隔离。</summary>
        public IDbTransaction BeginTransaction(IsolationLevel isolationLevel)
        {
            var transaction = BeginTransaction();
            Last!.RequestedIsolation = isolationLevel;
            return transaction;
        }
        [AllowNull] public string ConnectionString { get; set; } = "";
        public int ConnectionTimeout => 0;
        public string Database => "offline-product-bom";
        public ConnectionState State => ConnectionState.Open;
        public void Open() { }
        public void Close() { }
        /// <summary>记录故障清理时的连接关闭，不让夹具执行真实数据库行为。</summary>
        public void Dispose()
        {
            Disposals++;
            if (FailDispose) throw new InvalidOperationException("受控连接释放失败");
        }
        public void ChangeDatabase(string databaseName) => throw new NotSupportedException();
        public IDbCommand CreateCommand() => throw new NotSupportedException();
    }

    /// <summary>提交只记录计数，回滚恢复全部字段；不模拟 SQL 锁或实库并发。</summary>
    private sealed class TransactionStub(Dictionary<Guid, Product> products, Dictionary<Guid, Bom> boms,
        Dictionary<Guid, ProductAttributeSelection> selections, Dictionary<Guid, ProductPackage> packages, ConnectionStub connection) : IDbTransaction
    {
        private readonly Dictionary<Guid, Product> _products = products.ToDictionary(x => x.Key, x => Clone(x.Value));
        private readonly Dictionary<Guid, Bom> _boms = boms.ToDictionary(x => x.Key, x => Clone(x.Value));
        private readonly Dictionary<Guid, ProductAttributeSelection> _selections = selections.ToDictionary(x => x.Key, x => Clone(x.Value));
        private readonly Dictionary<Guid, ProductPackage> _packages = packages.ToDictionary(x => x.Key, x => Clone(x.Value));
        public int Commits;
        public int Rollbacks;
        public int Disposals;
        public IDbConnection Connection => connection;
        public IsolationLevel RequestedIsolation { get; set; } = IsolationLevel.ReadCommitted;
        public IsolationLevel IsolationLevel => RequestedIsolation;
        public bool IsCommitted { get; private set; }
        /// <summary>明确序列化失败发生在提交前，未知故障可发生于提交前或确认丢失时。</summary>
        public void Commit()
        {
            Commits++;
            if (connection.SerializationOnCommit) throw new SerializationFailureStub();
            if (connection.FailCommitBefore) throw new InvalidOperationException("受控提交前失败");
            IsCommitted = true;
            if (connection.FailCommitAfter) throw new InvalidOperationException("受控提交确认丢失");
        }
        /// <summary>完整恢复事务前产品及历史组成，捕捉后序写入失败。</summary>
        public void Rollback()
        {
            Rollbacks++;
            if (connection.FailRollback) throw new InvalidOperationException("受控回滚失败");
            if (IsCommitted) return;
            products.Clear();
            boms.Clear();
            selections.Clear();
            foreach (var pair in _products) products.Add(pair.Key, Clone(pair.Value));
            foreach (var pair in _boms) boms.Add(pair.Key, Clone(pair.Value));
            foreach (var pair in _selections) selections.Add(pair.Key, Clone(pair.Value));
            packages.Clear(); foreach (var pair in _packages) packages.Add(pair.Key, Clone(pair.Value));
        }
        /// <summary>记录释放，用于核对保存入口未接管外层事务。</summary>
        public void Dispose()
        {
            Disposals++;
            if (connection.FailTransactionDispose) throw new InvalidOperationException("受控事务释放失败");
        }
    }

    /// <summary>只模拟已确认的序列化终止信号，不模拟实库并发算法。</summary>
    private sealed class SerializationFailureStub : Exception { }

    /// <summary>将局部故障日志对象提供给真实服务，不修改全局日志配置。</summary>
    private sealed class FaultLoggerFactory(FaultLogger logger) : ILoggerFactory
    {
        /// <summary>返回本场景日志对象。</summary>
        public ILogger CreateLogger(string categoryName) => logger;
        /// <summary>夹具不安装外部日志提供者。</summary>
        public void AddProvider(ILoggerProvider provider) { }
        /// <summary>夹具没有外部日志资源。</summary>
        public void Dispose() { }
    }

    /// <summary>记录真实诊断调用并按场景注入故障，避免测试跳过日志路径。</summary>
    private sealed class FaultLogger : ILogger
    {
        public bool Fail;
        public int Calls;
        /// <summary>允许诊断到达本夹具。</summary>
        public bool IsEnabled(LogLevel logLevel) => true;
        /// <summary>局部夹具无需日志范围。</summary>
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        /// <summary>记录调用并抛出配置的日志异常。</summary>
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Calls++;
            if (Fail) throw new InvalidOperationException("受控日志失败");
        }
    }
}
