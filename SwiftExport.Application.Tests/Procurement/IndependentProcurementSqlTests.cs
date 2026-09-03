using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Dapper;
using SwiftExport.Application.Interfaces.MasterData;
using SwiftExport.Application.Interfaces.Procurement;
using SwiftExport.Application.Services.Procurement.OrderManagement;
using SwiftExport.Contracts.DTOs.MasterData.ExecutionEligibility;
using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderDetailDto;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Core.Interfaces.Procurement;
using SwiftExport.Infrastructure.Data;

namespace SwiftExport.Application.Tests.Procurement;

/// <summary>使用真实隔离 LocalDB 验证采购事务、快照、幂等及主数据资格；必须显式启用环境开关。</summary>
[TestClass]
[DoNotParallelize]
public sealed class IndependentProcurementSqlTests
{
    /// <summary>将新单 Uid 交给全部旧采购读写入口，必须因所有权被拒绝且不产生旧日志或报告。</summary>
    [TestMethod]
    public async Task LegacyWorkspaceCannotReadOrMutateIndependentOrder()
    {
        var created = await Db.CreateAsync();
        Assert.IsTrue(created.Success, created.Message);
        var uid = created.Data.PurchaseOrderId;
        var lineId = created.Data.Lines.Single().PurchaseOrderLineId;
        await Db.InScopeAsync(async provider =>
        {
            var old = provider.GetRequiredService<IPurchaseOrderWorkspaceService>();
            var search = await old.SearchAsync(new SwiftExport.Contracts.DTOs.Procurement.PurchaseOrders.PurchaseOrderSearchRequest());
            Assert.IsTrue(search.Success, search.Message);
            Assert.AreEqual(0, search.Data.TotalCount);
            var detail = await old.GetDetailAsync(uid);
            Assert.IsFalse(detail.Success);
            StringAssert.Contains(detail.Message, "旧采购执行链");
            var trace = await old.GetTraceAsync(uid);
            Assert.IsFalse(trace.Success);
            StringAssert.Contains(trace.Message, "旧采购执行链");
            var progress = await old.GetProgressAsync(uid);
            Assert.IsFalse(progress.Success);
            StringAssert.Contains(progress.Message, "旧采购执行链");
            var writes = new[]
            {
                await old.UpdateDraftAsync(uid, new() { CommandId=Guid.NewGuid(),ExpectedVersion=1,
                    Lines=[new() {PurchaseOrderLineId=lineId,ExpectedVersion=1,TaxPrice=3m}] }),
                await old.ConfirmAsync(uid, new() {CommandId=Guid.NewGuid(),ExpectedVersion=1}),
                await old.CancelConfirmationAsync(uid, new() {CommandId=Guid.NewGuid(),ExpectedVersion=1,Reason="跨链测试"}),
                await old.ReportProgressAsync(uid, new() {CommandId=Guid.NewGuid(),ExpectedPurchaseOrderVersion=1,
                    PurchaseOrderLineId=lineId,ExpectedLineVersion=1,ReadyQty=1})
            };
            foreach (var result in writes)
            {
                Assert.IsFalse(result.Success);
                StringAssert.Contains(result.Message, "采购单不存在");
            }
            return true;
        });
        Assert.AreEqual(1, await Db.ScalarAsync<int>("SELECT Version FROM dbo.PurchaseOrderHead WHERE Uid=@uid", new { uid }));
        Assert.AreEqual(0, await Db.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.SupplierProgressReport"));
        Assert.AreEqual(0, await Db.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.BusinessOperationLog WHERE Module=N'Procurement'"));
    }

    private IndependentProcurementSqlFixture? fixture;
    private IndependentProcurementSqlFixture Db => fixture!;

    /// <summary>未显式开启时标记未执行，绝不悄悄改为假仓储测试。</summary>
    [TestInitialize]
    public async Task InitializeAsync()
    {
        if (Environment.GetEnvironmentVariable("SWIFTEXPORT_RUN_SQL_TESTS") != "1")
            Assert.Inconclusive("设置 SWIFTEXPORT_RUN_SQL_TESTS=1 后运行隔离 LocalDB 集成测试。");
        fixture = new IndependentProcurementSqlFixture();
        await fixture.InitializeAsync();
    }

    /// <summary>每个测试仅删除自己创建的唯一测试库，失败测试也执行清理。</summary>
    [TestCleanup]
    public async Task CleanupAsync()
    {
        if (fixture != null) await fixture.DisposeAsync();
    }

    /// <summary>无需求链创建合法订单并冻结客户、单位、实际供应商和独立收货目标。</summary>
    [TestMethod]
    public async Task CreateAndConfirmWithoutBomDoesNotCreateDemandOrInventory()
    {
        var created = await Db.CreateAsync();
        Assert.IsTrue(created.Success, created.Message);
        var uid = created.Data.PurchaseOrderId;
        Assert.AreEqual(Db.SupplierId, await Db.ScalarAsync<Guid>("SELECT SupplierId FROM dbo.PurchaseOrderHead WHERE Uid=@uid", new { uid }));
        Assert.AreEqual(Db.CustomerId, await Db.ScalarAsync<Guid>("SELECT CustomerId FROM dbo.PurchaseOrderHead WHERE Uid=@uid", new { uid }));
        Assert.IsNull(await Db.ScalarAsync<Guid?>("SELECT DeliveryTargetSupplierId FROM dbo.PurchaseOrderLine WHERE PurchaseOrderId=@uid", new { uid }));
        Assert.AreEqual("PCS", await Db.ScalarAsync<string>("SELECT UnitSnapshot FROM dbo.PurchaseOrderLine WHERE PurchaseOrderId=@uid", new { uid }));
        Assert.AreEqual(10m, await Db.ScalarAsync<decimal>("SELECT UnlinkedQty FROM dbo.PurchaseOrderLine WHERE PurchaseOrderId=@uid", new { uid }));
        Assert.AreEqual(0, await Db.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.PurchaseOrderHead WHERE Uid=@uid AND (DemandSourceUid IS NOT NULL OR DemandSourceType IS NOT NULL OR DemandSourceNo IS NOT NULL)", new { uid }));
        var context = await Db.ContextAsync(uid);
        Assert.IsTrue(context.Success, context.Message);
        Assert.IsFalse(context.Data.Lines.Single().Bom.HasBom);
        var confirmed = await Db.ConfirmAsync(uid, ConfirmCommand(context.Data));
        Assert.IsTrue(confirmed.Success, confirmed.Message);
        Assert.AreEqual(1, await Db.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.PurchaseConfirmationLine WHERE PurchaseOrderId=@uid AND HasBom=0", new { uid }));
        Assert.AreEqual(0, await Db.ScalarAsync<int>("SELECT (SELECT COUNT(*) FROM dbo.GrossDemand)+(SELECT COUNT(*) FROM dbo.NetDemand)+(SELECT COUNT(*) FROM dbo.NetDemandAllocation)+(SELECT COUNT(*) FROM dbo.DemandExecutionLink)+(SELECT COUNT(*) FROM dbo.InventoryLedgerEntry)"));
    }

    /// <summary>客供件必须显式提交送达供应商，服务端保存所选身份快照而不从收货仓推导。</summary>
    [TestMethod]
    public async Task CustomerSuppliedLineRequiresExplicitDeliverySupplier()
    {
        await Db.ExecuteAsync("UPDATE dbo.Product SET MaterialType=N'客供件' WHERE Uid=@uid", new { uid = Db.ProductId });
        var missing = await Db.CreateAsync();
        Assert.IsFalse(missing.Success);
        StringAssert.Contains(missing.Message, "送达供应商");

        var command = Db.CreateCommand();
        command.Lines[0].DeliverySupplierId = Db.SupplierId;
        var created = await Db.CreateAsync(command);
        Assert.IsTrue(created.Success, created.Message);
        var uid = created.Data.PurchaseOrderId;
        Assert.AreEqual(Db.SupplierId, await Db.ScalarAsync<Guid>("SELECT DeliveryTargetSupplierId FROM dbo.PurchaseOrderLine WHERE PurchaseOrderId=@uid", new { uid }));
        Assert.AreEqual("TEST-ACTUAL", await Db.ScalarAsync<string>("SELECT DeliverySupplierCodeSnapshot FROM dbo.PurchaseOrderLine WHERE PurchaseOrderId=@uid", new { uid }));
    }

    /// <summary>商务保存改变价格不改变身份，拒绝过期版本及替换行，并重放原始创建回执。</summary>
    [TestMethod]
    public async Task SaveKeepsIdentityAndCreateReplayReturnsOriginalVersion()
    {
        var command = Db.CreateCommand();
        var created = await Db.CreateAsync(command);
        Assert.IsTrue(created.Success, created.Message);
        var uid = created.Data.PurchaseOrderId;
        var save = SaveCommand(created.Data);
        var saved = await Db.SaveAsync(uid, save);
        Assert.IsTrue(saved.Success, saved.Message);
        Assert.AreEqual(2, saved.Data.Version);
        var stale = SaveCommand(created.Data);
        Assert.IsFalse((await Db.SaveAsync(uid, stale)).Success);
        var swapped = SaveCommand(saved.Data);
        swapped.Lines[0].PurchaseOrderLineId = Guid.NewGuid();
        Assert.IsFalse((await Db.SaveAsync(uid, swapped)).Success);
        Assert.AreEqual(Db.ProductId, await Db.ScalarAsync<Guid>("SELECT ProductId FROM dbo.PurchaseOrderLine WHERE PurchaseOrderId=@uid", new { uid }));
        Assert.AreEqual(10m, await Db.ScalarAsync<decimal>("SELECT QTY FROM dbo.PurchaseOrderLine WHERE PurchaseOrderId=@uid", new { uid }));
        await Db.ExecuteAsync("UPDATE dbo.Supplier SET SupplierName=N'当前名称已改变' WHERE Uid=@uid", new { uid = Db.SupplierId });
        var replay = await Db.CreateAsync(command);
        Assert.IsTrue(replay.Success, replay.Message);
        Assert.AreEqual(1, replay.Data.Version);
        Assert.AreEqual(uid, replay.Data.PurchaseOrderId);
        Assert.AreEqual("实际供应商", await Db.ScalarAsync<string>("SELECT SupplierName FROM dbo.PurchaseOrderHead WHERE Uid=@uid", new { uid }));
        command.Lines[0].Quantity = 20;
        Assert.IsFalse((await Db.CreateAsync(command)).Success);
        command.Lines[0].Quantity = 10;
        Db.ActorId = Db.OtherActorId;
        Assert.IsFalse((await Db.CreateAsync(command)).Success);
        Assert.AreEqual(2, await Db.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.ProcurementCommandReceipt"));
    }

    /// <summary>客供件全路径显式选择消耗仓，冻结比例和仓库快照且不误用收货供应商。</summary>
    [TestMethod]
    public async Task BomConfirmationRequiresExactBindingsAndPersistsConsumptionSnapshot()
    {
        await Db.AddBomAsync();
        var created = await Db.CreateAsync();
        Assert.IsTrue(created.Success, created.Message);
        var uid = created.Data.PurchaseOrderId;
        var context = await Db.ContextAsync(uid);
        Assert.IsTrue(context.Success, context.Message);
        var command = ConfirmCommand(context.Data);
        Assert.IsFalse((await Db.ConfirmAsync(uid, command)).Success);
        Assert.AreEqual(0, await Db.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.PurchaseConfirmationLine"));
        AddBindings(command, context.Data);
        var confirmed = await Db.ConfirmAsync(uid, command);
        Assert.IsTrue(confirmed.Success, confirmed.Message);
        Assert.AreEqual(Db.ConsumptionSiteId, await Db.ScalarAsync<Guid>("SELECT ConsumptionWarehouseSiteId FROM dbo.PurchaseConfirmationNode WHERE IsCustomerSupplied=1"));
        Assert.AreEqual(Db.ConsumptionSupplierId, await Db.ScalarAsync<Guid>("SELECT ConsumptionSupplierId FROM dbo.PurchaseConfirmationNode WHERE IsCustomerSupplied=1"));
        Assert.AreEqual(0.6667m, await Db.ScalarAsync<decimal>("SELECT CumulativeRatio FROM dbo.PurchaseConfirmationNode WHERE IsCustomerSupplied=1"));
        await Db.ExecuteAsync("UPDATE dbo.Bom SET Numerator=7,Version=Version+1 WHERE Uid=@uid", new { uid = Db.BomLineId });
        await Db.ExecuteAsync("UPDATE dbo.Product SET IsActive=0 WHERE Uid=@uid", new { uid = Db.ProductId });
        var history = await Db.InScopeAsync(p => p.GetRequiredService<IProcurementConfirmationQueryService>().GetSnapshotAsync(uid));
        Assert.IsTrue(history.Success, history.Message);
        var frozen = history.Data.Lines.Single().Nodes.Single(n => n.Structure.IsCustomerSupplied);
        Assert.AreEqual(0.6667m, frozen.Structure.CumulativeRatio);
        Assert.AreEqual(Db.ConsumptionSiteId, frozen.ConsumptionWarehouseSiteId);
        var replay = await Db.ConfirmAsync(uid, command);
        Assert.IsTrue(replay.Success, replay.Message);
        Assert.AreEqual(confirmed.Data.ConfirmationId, replay.Data.ConfirmationId);
        Assert.AreEqual(1, await Db.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.PurchaseConfirmationLine"));
    }

    /// <summary>上下文读取之后 BOM 改变时拒绝旧指纹，失败不产生确认状态或快照。</summary>
    [TestMethod]
    public async Task ChangedBomContextRejectsConfirmationWithoutPartialWrites()
    {
        await Db.AddBomAsync();
        var created = await Db.CreateAsync();
        Assert.IsTrue(created.Success, created.Message);
        var context = await Db.ContextAsync(created.Data.PurchaseOrderId);
        Assert.IsTrue(context.Success, context.Message);
        var command = ConfirmCommand(context.Data);
        AddBindings(command, context.Data);
        await Db.ExecuteAsync("UPDATE dbo.Bom SET Numerator=3,Version=Version+1 WHERE Uid=@uid", new { uid = Db.BomLineId });
        var failed = await Db.ConfirmAsync(created.Data.PurchaseOrderId, command);
        Assert.IsFalse(failed.Success);
        Assert.AreEqual(0, await Db.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.PurchaseConfirmationLine"));
        Assert.AreEqual(0, await Db.ScalarAsync<int>("SELECT ConfirmationStatus FROM dbo.PurchaseOrderHead"));
        Assert.AreEqual(1, await Db.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.ProcurementCommandReceipt"));
    }

    /// <summary>缺失主数据、失效单位或缺失 BOM 子项均阻止新事实形成，不能回退成无 BOM。</summary>
    [TestMethod]
    public async Task MissingMasterInvalidUnitAndBrokenBomAreRejected()
    {
        var missing = Db.CreateCommand();
        missing.CustomerId = Guid.NewGuid();
        Assert.IsFalse((await Db.CreateAsync(missing)).Success);
        await Db.ExecuteAsync("UPDATE dbo.SysDictData SET IsEnabled=0");
        Assert.IsFalse((await Db.CreateAsync()).Success);
        Assert.AreEqual(0, await Db.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.PurchaseOrderHead"));
        await Db.ExecuteAsync("UPDATE dbo.SysDictData SET IsEnabled=1");
        await Db.AddBomAsync();
        var created = await Db.CreateAsync();
        Assert.IsTrue(created.Success, created.Message);
        await Db.ExecuteAsync("UPDATE dbo.Bom SET ComponentProductId=NEWID() WHERE Uid=@uid", new { uid = Db.BomLineId });
        var context = await Db.ContextAsync(created.Data.PurchaseOrderId);
        Assert.IsFalse(context.Success);
        Assert.AreEqual(0, await Db.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.PurchaseConfirmationLine"));
    }

    /// <summary>两个并发确认只有一个成功；版本锁和事务确保只形成一批快照。</summary>
    [TestMethod]
    public async Task ConcurrentConfirmationsProduceOneSnapshotBatch()
    {
        var created = await Db.CreateAsync();
        Assert.IsTrue(created.Success, created.Message);
        var uid = created.Data.PurchaseOrderId;
        var context = await Db.ContextAsync(uid);
        Assert.IsTrue(context.Success, context.Message);
        var results = await Task.WhenAll(Db.ConfirmAsync(uid, ConfirmCommand(context.Data)), Db.ConfirmAsync(uid, ConfirmCommand(context.Data)));
        Assert.AreEqual(1, results.Count(r => r.Success));
        Assert.AreEqual(1, await Db.ScalarAsync<int>("SELECT COUNT(DISTINCT ConfirmationId) FROM dbo.PurchaseConfirmationLine"));
        Assert.AreEqual(2, await Db.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.ProcurementCommandReceipt"));
    }

    /// <summary>真实日志 SQL 故障回滚采购头、行、编号序列和幂等回执。</summary>
    [TestMethod]
    public async Task LogFailureRollsBackCreationAndCommandReceipt()
    {
        await Db.RejectLogsAsync();
        var failed = await Db.CreateAsync();
        Assert.IsFalse(failed.Success);
        Assert.AreEqual(0, await Db.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.PurchaseOrderHead"));
        Assert.AreEqual(0, await Db.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.PurchaseOrderLine"));
        Assert.AreEqual(0, await Db.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.ProcurementCommandReceipt"));
        Assert.AreEqual(0, await Db.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.DocumentSequence"));
    }

    /// <summary>确认已写入快照后日志失败，必须回滚整批快照与头状态；重试仍可使用同命令。</summary>
    [TestMethod]
    public async Task ConfirmationLogFailureRollsBackSnapshotAndAllowsRetry()
    {
        await Db.AddBomAsync();
        var created = await Db.CreateAsync();
        Assert.IsTrue(created.Success, created.Message);
        var uid = created.Data.PurchaseOrderId;
        var context = await Db.ContextAsync(uid);
        Assert.IsTrue(context.Success, context.Message);
        var command = ConfirmCommand(context.Data);
        AddBindings(command, context.Data);
        await Db.RejectLogsAsync();
        Assert.IsFalse((await Db.ConfirmAsync(uid, command)).Success);
        Assert.AreEqual(0, await Db.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.PurchaseConfirmationLine"));
        Assert.AreEqual(0, await Db.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.PurchaseConfirmationNode"));
        Assert.AreEqual(0, await Db.ScalarAsync<int>("SELECT ConfirmationStatus FROM dbo.PurchaseOrderHead"));
        Assert.AreEqual(1, await Db.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.ProcurementCommandReceipt"));
        await Db.ExecuteAsync("DROP TRIGGER dbo.TR_Test_RejectLog;");
        var retried = await Db.ConfirmAsync(uid, command);
        Assert.IsTrue(retried.Success, retried.Message);
    }

    /// <summary>合法 BOM 没有客供件仍保存结构依据，与根本没有 BOM 的订单明确区分。</summary>
    [TestMethod]
    public async Task BomWithoutCustomerSuppliedNodesKeepsBomSnapshotWithoutConsumptionBinding()
    {
        await Db.AddBomAsync();
        await Db.ExecuteAsync("UPDATE dbo.Product SET MaterialType=N'零件' WHERE Uid=@uid", new { uid = Db.ComponentId });
        var created = await Db.CreateAsync();
        Assert.IsTrue(created.Success, created.Message);
        var uid = created.Data.PurchaseOrderId;
        var context = await Db.ContextAsync(uid);
        Assert.IsTrue(context.Success, context.Message);
        Assert.IsTrue(context.Data.Lines.Single().Bom.HasBom);
        Assert.IsFalse(context.Data.Lines.Single().Bom.Nodes.Any(n => n.IsCustomerSupplied));
        var confirmed = await Db.ConfirmAsync(uid, ConfirmCommand(context.Data));
        Assert.IsTrue(confirmed.Success, confirmed.Message);
        Assert.AreEqual(1, await Db.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.PurchaseConfirmationLine WHERE HasBom=1"));
        Assert.AreEqual(1, await Db.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.PurchaseConfirmationNode WHERE ConsumptionWarehouseSiteId IS NULL"));
    }

    /// <summary>真实账号停用后即使已有成功命令回执也拒绝重放，不通过旧回执绕过权限。</summary>
    [TestMethod]
    public async Task RevokedAccountCannotReplayPreviouslySuccessfulCommand()
    {
        var command = Db.CreateCommand();
        var created = await Db.CreateAsync(command);
        Assert.IsTrue(created.Success, created.Message);
        await Db.ExecuteAsync("UPDATE dbo.SysUser SET IsActive=0 WHERE Uid=@uid", new { uid = Db.ActorId });
        Assert.IsFalse((await Db.CreateAsync(command)).Success);
        Assert.AreEqual(1, await Db.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.ProcurementCommandReceipt"));
    }

    /// <summary>非管理员只授予创建角色时不能读取、编辑或确认；撤销真实角色关联后连成功回执也不能重放。</summary>
    [TestMethod]
    public async Task OperationRoleGrantsOnlyItsCommandAndRevocationBlocksReplay()
    {
        await Db.ExecuteAsync("UPDATE dbo.SysRoles SET RoleName=N'Procurement.Order.Create' WHERE Uid=@uid", new { uid = Db.RoleId });
        var command = Db.CreateCommand();
        var created = await Db.CreateAsync(command);
        Assert.IsTrue(created.Success, created.Message);
        var uid = created.Data.PurchaseOrderId;
        var query = await Db.ContextAsync(uid);
        Assert.IsFalse(query.Success);
        StringAssert.Contains(query.Message, "权限");
        var save = await Db.SaveAsync(uid, SaveCommand(created.Data));
        Assert.IsFalse(save.Success);
        StringAssert.Contains(save.Message, "权限");
        var confirm = await Db.ConfirmAsync(uid, new ConfirmProcurementOrderCommand
        {
            CommandId = Guid.NewGuid(), ExpectedVersion = created.Data.Version, ContextFingerprint = "不应越权读取依据"
        });
        Assert.IsFalse(confirm.Success);
        StringAssert.Contains(confirm.Message, "权限");
        await Db.ExecuteAsync("UPDATE dbo.SysUser_Roles SET IsDeleted=1 WHERE UserId=@uid", new { uid = Db.ActorId });
        var replay = await Db.CreateAsync(command);
        Assert.IsFalse(replay.Success);
        StringAssert.Contains(replay.Message, "权限");
        Assert.AreEqual(1, await Db.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.ProcurementCommandReceipt"));
        Assert.AreEqual(1, await Db.ScalarAsync<int>("SELECT Version FROM dbo.PurchaseOrderHead WHERE Uid=@uid", new { uid }));
    }

    /// <summary>启停保留版本与日志原子性，并以当前有效角色而不是用户名快照授权。</summary>
    [TestMethod]
    public async Task EligibilityChecksRoleVersionAndRollsBackLogFailures()
    {
        var command = new SetExecutionEligibilityStatusCommand
        {
            Kind = ExecutionEligibilityKind.Supplier, Uid = Db.SupplierId, ExpectedVersion = 1,
            IsActive = false, Reason = "暂停新增采购"
        };
        var disabled = await Db.InScopeAsync(p => p.GetRequiredService<IExecutionEligibilityService>().SetStatusAsync(command));
        Assert.IsTrue(disabled.Success, disabled.Message);
        Assert.IsFalse((await Db.CreateAsync()).Success);
        Assert.IsFalse((await Db.InScopeAsync(p => p.GetRequiredService<IExecutionEligibilityService>().SetStatusAsync(command))).Success);
        command.ExpectedVersion = 2;
        command.IsActive = true;
        await Db.ExecuteAsync("UPDATE dbo.SysRoles SET IsDeleted=1 WHERE Uid=@uid", new { uid = Db.RoleId });
        Assert.IsFalse((await Db.InScopeAsync(p => p.GetRequiredService<IExecutionEligibilityService>().SetStatusAsync(command))).Success);
        await Db.ExecuteAsync("UPDATE dbo.SysRoles SET IsDeleted=0 WHERE Uid=@uid", new { uid = Db.RoleId });
        await Db.RejectLogsAsync();
        await Assert.ThrowsAsync<SqlException>(() => Db.InScopeAsync(p => p.GetRequiredService<IExecutionEligibilityService>().SetStatusAsync(command)));
        Assert.IsFalse(await Db.ScalarAsync<bool>("SELECT IsActive FROM dbo.Supplier WHERE Uid=@uid", new { uid = Db.SupplierId }));
        Assert.AreEqual(2, await Db.ScalarAsync<int>("SELECT Version FROM dbo.Supplier WHERE Uid=@uid", new { uid = Db.SupplierId }));
        Assert.AreEqual(1, await Db.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.BusinessOperationLog WHERE Module=N'MasterData'"));
    }

    /// <summary>已有采购引用后禁止直接修改产品基础单位，余额是否为零不参与放行。</summary>
    [TestMethod]
    public async Task ReferencedProductUnitCannotBeChanged()
    {
        var created = await Db.CreateAsync();
        Assert.IsTrue(created.Success, created.Message);
        await Assert.ThrowsAsync<SqlException>(() => Db.ExecuteAsync("UPDATE dbo.Product SET Unit=N'KG' WHERE Uid=@uid", new { uid = Db.ProductId }));
        Assert.AreEqual("PCS", await Db.ScalarAsync<string>("SELECT Unit FROM dbo.Product WHERE Uid=@uid", new { uid = Db.ProductId }));
    }

    /// <summary>草稿形成后改变产品物料角色，当前结构上下文和正式确认都拒绝身份含义漂移。</summary>
    [TestMethod]
    public async Task ProductRoleDriftRejectsContextAndConfirmation()
    {
        var created = await Db.CreateAsync();
        Assert.IsTrue(created.Success, created.Message);
        var uid = created.Data.PurchaseOrderId;
        var before = await Db.ContextAsync(uid);
        Assert.IsTrue(before.Success, before.Message);
        await Db.ExecuteAsync("UPDATE dbo.Product SET MaterialType=N'客供件',Version=Version+1 WHERE Uid=@uid", new { uid = Db.ProductId });
        Assert.IsFalse((await Db.ContextAsync(uid)).Success);
        Assert.IsFalse((await Db.ConfirmAsync(uid, ConfirmCommand(before.Data))).Success);
        Assert.AreEqual(0, await Db.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.PurchaseConfirmationLine"));
    }

    /// <summary>正在读取确认依据时，第二连接修改 BOM 被数据库锁阻塞；提交后新读取产生不同指纹。</summary>
    [TestMethod]
    public async Task BomUpdateWaitsForCoherentReadTransaction()
    {
        await Db.AddBomAsync();
        var created = await Db.CreateAsync();
        Assert.IsTrue(created.Success, created.Message);
        var uid = created.Data.PurchaseOrderId;
        var fingerprint = await ReadWhileWriterIsBlockedAsync(uid,
            "UPDATE dbo.Bom SET Numerator=5,Version=Version+1 WHERE Uid=@BomLineId;",
            new { Db.BomLineId });
        var after = await Db.ContextAsync(uid);
        Assert.IsTrue(after.Success, after.Message);
        Assert.AreNotEqual(fingerprint, after.Data.Fingerprint);
    }

    /// <summary>无 BOM 的空组成范围同样持锁；并发插入等待事务结束，之后非法隐藏组成不能被当作无 BOM。</summary>
    [TestMethod]
    public async Task EmptyBomRangeBlocksConcurrentInsertion()
    {
        var created = await Db.CreateAsync();
        Assert.IsTrue(created.Success, created.Message);
        var uid = created.Data.PurchaseOrderId;
        await ReadWhileWriterIsBlockedAsync(uid, """
            INSERT dbo.Bom(Uid,BomNo,BomVersion,ParentProductId,ParentSkuSnapshot,LineNumber,ComponentProductId,ComponentSkuSnapshot,Numerator,Denominator)
            VALUES(@BomLineId,N'TEST-BOM',1,@ProductId,N'TEST-FG',1,@ComponentId,N'TEST-B',2,3);
            """, new { Db.BomLineId, Db.ProductId, Db.ComponentId });
        Assert.IsFalse((await Db.ContextAsync(uid)).Success);
    }

    /// <summary>真实旧版表上已有采购先升级再重跑迁移，保留历史字段且不猜补客户、单位、无来源数量或 BOM 快照。</summary>
    [TestMethod]
    public async Task MigrationPreservesLegacyRowsAndCanRunTwice()
    {
        await using var legacy = new IndependentProcurementSqlFixture();
        await legacy.InitializeAsync(includeLegacyOrder: true);
        for (var pass = 0; pass < 2; pass++)
        {
            Assert.AreEqual(7, await legacy.ScalarAsync<int>("SELECT Version FROM dbo.PurchaseOrderHead WHERE Uid=@uid", new { uid = legacy.LegacyOrderId }));
            Assert.AreEqual("迁移前原始备注", await legacy.ScalarAsync<string>("SELECT Remark FROM dbo.PurchaseOrderHead WHERE Uid=@uid", new { uid = legacy.LegacyOrderId }));
            Assert.AreEqual("OLD-DEMAND-001", await legacy.ScalarAsync<string>("SELECT DemandSourceNo FROM dbo.PurchaseOrderHead WHERE Uid=@uid", new { uid = legacy.LegacyOrderId }));
            Assert.AreEqual(12.25m, await legacy.ScalarAsync<decimal>("SELECT QTY FROM dbo.PurchaseOrderLine WHERE Uid=@uid", new { uid = legacy.LegacyLineId }));
            Assert.AreEqual(1, await legacy.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.PurchaseOrderHead h JOIN dbo.PurchaseOrderLine l ON l.PurchaseOrderId=h.Uid WHERE h.Uid=@uid AND h.CustomerId IS NULL AND l.ProductId IS NULL AND l.UnitSnapshot IS NULL AND l.UnlinkedQty IS NULL", new { uid = legacy.LegacyOrderId }));
            Assert.AreEqual(0, await legacy.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.PurchaseConfirmationLine"));
            var detail = await legacy.InScopeAsync(p => p.GetRequiredService<IProcurementOrderQueryService>().GetDetailAsync(legacy.LegacyOrderId));
            Assert.IsFalse(detail.Success);
            var search = await legacy.InScopeAsync(p => p.GetRequiredService<IProcurementOrderQueryService>().SearchAsync(new ProcurementOrderSearchRequest()));
            Assert.IsTrue(search.Success, search.Message);
            Assert.AreEqual(0, search.Data.TotalCount);
            if (pass == 0) await legacy.ApplyMigrationAsync();
        }
    }

    /// <summary>查询 SQL Server 正在执行请求的 blocking_session_id，证明写入确实等待读取事务，而非仅等待线程调度。</summary>
    private async Task<string> ReadWhileWriterIsBlockedAsync(Guid uid, string sql, object args)
    {
        return await Db.InScopeAsync(async provider =>
        {
            var session = provider.GetRequiredService<DbSession>();
            using var transaction = session.Connection.BeginTransaction(System.Data.IsolationLevel.Serializable);
            session.Transaction = transaction;
            await using var writer = await Db.OpenConnectionAsync();
            var readerId = await session.Connection.ExecuteScalarAsync<int>("SELECT @@SPID", transaction: transaction);
            var writerId = await writer.ExecuteScalarAsync<int>("SELECT @@SPID");
            Task<int>? pending = null;
            try
            {
                var repository = provider.GetRequiredService<IProcurementOrderRepository>();
                var head = await repository.GetHeadAsync(uid, default);
                var lines = await repository.GetLinesAsync(uid, default);
                var context = await provider.GetRequiredService<ProcurementConfirmationBuilder>().BuildAsync(head!, lines, default);
                pending = writer.ExecuteAsync(sql, args, commandTimeout: 20);
                var blockedBy = 0;
                for (var attempt = 0; attempt < 30 && blockedBy != readerId; attempt++)
                {
                    await Task.Delay(100);
                    blockedBy = await Db.ScalarAsync<int>("SELECT COALESCE(MAX(blocking_session_id),0) FROM sys.dm_exec_requests WHERE session_id=@writerId", new { writerId });
                    if (pending.IsCompleted) break;
                }
                Assert.AreEqual(readerId, blockedBy, "并发 BOM 写入必须被读取事务真实阻塞。");
                Assert.IsFalse(pending.IsCompleted);
                transaction.Commit();
                session.Transaction = null;
                Assert.AreEqual(1, await pending);
                return context.Fingerprint;
            }
            finally
            {
                if (session.Transaction != null)
                {
                    transaction.Rollback();
                    session.Transaction = null;
                }
                if (pending != null && !pending.IsCompleted) await pending;
            }
        });
    }

    private static UpdatePurchaseOrderDraftCommand SaveCommand(ProcurementOrderCommandResult order) => new()
    {
        CommandId = Guid.NewGuid(), ExpectedVersion = order.Version, Remark = "商务修改",
        Lines = order.Lines.Select(l => new UpdatePurchaseOrderDraftLineCommand
        {
            PurchaseOrderLineId = l.PurchaseOrderLineId, ExpectedVersion = l.Version, TaxPrice = 3m,
            SupplierSku = "SUPPLIER-UPDATED", PlannedDeliveryDate = new DateTime(2026, 9, 10)
        }).ToList()
    };

    private static ConfirmProcurementOrderCommand ConfirmCommand(ProcurementConfirmationContextDto context) => new()
    {
        CommandId = Guid.NewGuid(), ExpectedVersion = context.Version, ContextFingerprint = context.Fingerprint
    };

    /// <summary>绑定服务端实际返回的每个客供件完整路径，避免测试假设 BOM 路径编码。</summary>
    private void AddBindings(ConfirmProcurementOrderCommand command, ProcurementConfirmationContextDto context)
    {
        command.ConsumptionBindings = context.Lines.SelectMany(l => l.Bom.Nodes.Where(n => n.IsCustomerSupplied).Select(n =>
            new ProcurementConsumptionBinding { PurchaseOrderLineId = l.PurchaseOrderLineId, Path = n.Path, WarehouseSiteId = Db.ConsumptionSiteId })).ToList();
    }
}
