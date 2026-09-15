using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Interfaces.Procurement;
using SwiftExport.Application.Interfaces.DemandPlanning;
using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;
using SwiftExport.Contracts.SysEnums;

namespace SwiftExport.Application.Tests.Procurement;

/// <summary>在独立 LocalDB 实例验证反向操作的真实事务、版本和回执，不访问业务库。</summary>
[TestClass, DoNotParallelize]
public sealed class ProcurementReversalSqlTests
{
    /// <summary>真实并发相同命令只写一次；不同命令使用旧版本不得覆盖已经提交的状态。</summary>
    [TestMethod]
    public async Task ConcurrentReplayWritesOneReceiptAndRejectsStaleVersion()
    {
        await using var db = new IndependentProcurementSqlFixture();
        await db.InitializeAsync();
        await db.PrepareReversalSchemaAsync();
        var created = await db.CreateAsync();
        Assert.IsTrue(created.Success, created.Message);
        var uid = created.Data!.PurchaseOrderId;
        var command = new VoidProcurementOrderCommand { CommandId = Guid.NewGuid(), ExpectedVersion = created.Data.Version, Reason = "并发重复提交" };
        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => db.InScopeAsync(p => p.GetRequiredService<IProcurementOrderReversalService>().VoidAsync(uid, command))));
        Assert.IsTrue(results.All(x => x.Success), string.Join(";", results.Select(x => x.Message)));
        Assert.AreEqual(results[0].Data!.Version, results[1].Data!.Version);
        Assert.AreEqual(1, await db.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.ProcurementCommandReceipt WHERE CommandId=@CommandId", command));
        Assert.AreEqual(1, await db.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.BusinessOperationLog WHERE CommandId=@CommandId", command));
        var stale = await db.InScopeAsync(p => p.GetRequiredService<IProcurementOrderReversalService>().VoidAsync(uid,
            new() { CommandId = Guid.NewGuid(), ExpectedVersion = created.Data.Version, Reason = "旧版本必须拒绝" }));
        Assert.IsFalse(stale.Success);
        Assert.AreEqual(SwiftExport.Contracts.DTOs.Errors.ServiceErrorCodes.VersionConflict, stale.Error?.Code);
    }

    /// <summary>同来源的另一张活动采购继续阻断物料整组回退；全部释放后原 Link 仍作为历史保留。</summary>
    [TestMethod]
    public async Task DemandReferencesReleaseOnlySelectedContributionAndBlockGroupRollback()
    {
        await using var db = new IndependentProcurementSqlFixture();
        await db.InitializeAsync();
        await db.PrepareReversalSchemaAsync();
        var command = db.CreateCommand();
        command.Lines.Add(new() { ProductId = db.ProductId, Quantity = 3m, UnitPrice = 1m, TaxPrice = 1m, WarehouseSiteId = db.ReceiptSiteId });
        var first = await db.CreateAsync(command);
        var second = await db.CreateAsync();
        Assert.IsTrue(first.Success, first.Message);
        Assert.IsTrue(second.Success, second.Message);
        var originalContext = await db.ContextAsync(first.Data!.PurchaseOrderId);
        first = await db.ConfirmAsync(first.Data.PurchaseOrderId, new() { CommandId = Guid.NewGuid(), ExpectedVersion = first.Data.Version, ContextFingerprint = originalContext.Data!.Fingerprint });
        Assert.IsTrue(first.Success, first.Message);
        var source = Guid.NewGuid(); var net = Guid.NewGuid();
        var uid = first.Data!.PurchaseOrderId; var other = second.Data!.PurchaseOrderId;
        var line = first.Data.Lines[0].PurchaseOrderLineId;
        var otherLine = second.Data.Lines[0].PurchaseOrderLineId;
        await db.ExecuteAsync("""
            INSERT dbo.GrossDemand(Uid,SourceType,SourceUid,SourceNo,SourceLineId,IsNonStandard,SkuSnapshot,DemandQty)
            VALUES(@source,N'Manual',NEWID(),N'TEST-SOURCE',NEWID(),0,N'TEST-FG',20);
            INSERT dbo.NetDemand(Uid,GrossDemandId,DecompositionRevision,LineNumber,RootNetDemandId,SupplyTypeSnapshot,SkuSnapshot,GrossQty,NetQty)
            VALUES(@net,@source,1,1,@net,N'采购',N'TEST-FG',20,20);
            UPDATE dbo.PurchaseOrderLine SET UnlinkedQty=0,SourceUid=@source WHERE Uid IN (@line,@otherLine);
            INSERT dbo.DemandExecutionLink(Uid,NetDemandId,ExecutionRequestId,CommandItemId,TargetType,TargetHeadId,TargetNo,TargetLineId,AllocatedQty)
            VALUES(NEWID(),@net,NEWID(),NEWID(),N'PO',@uid,N'FIRST',@line,10),
                  (NEWID(),@net,NEWID(),NEWID(),N'PO',@other,N'OTHER',@otherLine,10);
            """, new { source, net, uid, other, line, otherLine });
        var cancelled = await db.InScopeAsync(p => p.GetRequiredService<IProcurementOrderReversalService>().CancelConfirmationAsync(uid,
            new() { CommandId = Guid.NewGuid(), ExpectedVersion = first.Data.Version, Reason = "保留引用退回草稿" }));
        Assert.IsTrue(cancelled.Success, cancelled.Message);
        Assert.AreEqual(20m, await db.ScalarAsync<decimal>("SELECT SUM(AllocatedQty-ReleasedQty) FROM dbo.DemandExecutionLink WHERE NetDemandId=@net AND LinkStatus=0", new { net }));
        // 即使主数据此时增加 BOM，需求来源重新确认仍使用原已确认的无 BOM 依据。
        await db.AddBomAsync();
        var restoredContext = await db.ContextAsync(uid);
        Assert.IsTrue(restoredContext.Success, restoredContext.Message);
        Assert.IsTrue(restoredContext.Data!.Lines.All(x => !x.Bom.HasBom));
        Assert.AreEqual(originalContext.Data.Lines[0].Bom.Fingerprint, restoredContext.Data.Lines[0].Bom.Fingerprint);
        first = cancelled;
        var deletion = new DeleteProcurementDraftLinesCommand { CommandId = Guid.NewGuid(), ExpectedVersion = first.Data.Version, Reason = "只释放选定贡献", Lines = [first.Data.Lines[0]] };
        await db.RejectLogsAsync();
        var failed = await db.InScopeAsync(p => p.GetRequiredService<IProcurementOrderReversalService>().DeleteLinesAsync(uid, deletion));
        Assert.IsFalse(failed.Success);
        Assert.AreEqual(0, await db.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.PurchaseOrderLine WHERE PurchaseOrderId=@uid AND IsDeleted=1", new { uid }));
        Assert.AreEqual(20m, await db.ScalarAsync<decimal>("SELECT SUM(AllocatedQty-ReleasedQty) FROM dbo.DemandExecutionLink WHERE NetDemandId=@net AND LinkStatus=0", new { net }));
        await db.ExecuteAsync("DROP TRIGGER dbo.TR_Test_RejectLog;");
        var deleted = await db.InScopeAsync(p => p.GetRequiredService<IProcurementOrderReversalService>().DeleteLinesAsync(uid, deletion));
        Assert.IsTrue(deleted.Success, deleted.Message);
        Assert.AreEqual(10m, await db.ScalarAsync<decimal>("SELECT SUM(AllocatedQty-ReleasedQty) FROM dbo.DemandExecutionLink WHERE NetDemandId=@net AND LinkStatus=0", new { net }));
        var blocked = await db.InScopeAsync(p => p.GetRequiredService<IMaterialDemandEstablishmentService>().RollbackAsync(new() { CommandId = Guid.NewGuid(), Reason = "来源重做", Items = [new() { GrossDemandId = source, DecompositionRevision = 1 }] }));
        Assert.IsFalse(blocked.Success);
        Assert.AreEqual("DOWNSTREAM_EXECUTION_EXISTS", blocked.Error?.Code);
        var voided = await db.InScopeAsync(p => p.GetRequiredService<IProcurementOrderReversalService>().VoidAsync(other, new() { CommandId = Guid.NewGuid(), ExpectedVersion = second.Data.Version, Reason = "解除剩余引用" }));
        Assert.IsTrue(voided.Success, voided.Message);
        Assert.AreEqual(2, await db.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.DemandExecutionLink WHERE NetDemandId=@net AND LinkStatus=8 AND ReleasedQty=AllocatedQty", new { net }));
        var rollback = await db.InScopeAsync(p => p.GetRequiredService<IMaterialDemandEstablishmentService>().RollbackAsync(new() { CommandId = Guid.NewGuid(), Reason = "来源重做", Items = [new() { GrossDemandId = source, DecompositionRevision = 1 }] }));
        Assert.IsTrue(rollback.Success, rollback.Message);
        Assert.AreEqual(1, rollback.Data!.RolledBackSourceCount);
        Assert.AreEqual(1, await db.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.PurchaseOrderLine WHERE PurchaseOrderId=@uid AND IsDeleted=0", new { uid }));
    }

    /// <summary>取消只退确认状态；删行保留其他行；重试返回原回执；最后一行必须整单作废。</summary>
    [TestMethod]
    public async Task CancelDeleteAndVoidPreserveHistoryAndReplayReceipt()
    {
        await using var db = new IndependentProcurementSqlFixture();
        await db.InitializeAsync();
        await db.PrepareReversalSchemaAsync();
        var create = db.CreateCommand();
        create.Lines.Add(new() { ProductId = db.ProductId, Quantity = 3m, UnitPrice = 2m, TaxPrice = 2m, WarehouseSiteId = db.ReceiptSiteId });
        var created = await db.CreateAsync(create);
        Assert.IsTrue(created.Success, created.Message);
        var uid = created.Data!.PurchaseOrderId;
        var basis = await db.ContextAsync(uid);
        Assert.IsTrue(basis.Success, basis.Message);
        var confirmed = await db.ConfirmAsync(uid, new() { CommandId = Guid.NewGuid(), ExpectedVersion = created.Data.Version, ContextFingerprint = basis.Data!.Fingerprint });
        Assert.IsTrue(confirmed.Success, confirmed.Message);
        var batch = confirmed.Data!.ConfirmationId;
        var cancel = new CancelProcurementOrderConfirmationCommand { CommandId = Guid.NewGuid(), ExpectedVersion = confirmed.Data.Version, Reason = "删除部分需求后重新采购" };
        var cancelled = await db.InScopeAsync(p => p.GetRequiredService<IProcurementOrderReversalService>().CancelConfirmationAsync(uid, cancel));
        Assert.IsTrue(cancelled.Success, cancelled.Message);
        Assert.AreEqual(ConfirmationStatusEnum.待确认, cancelled.Data!.ConfirmationStatus);
        Assert.IsNull(cancelled.Data.ConfirmationId);
        Assert.AreEqual(2, await db.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.PurchaseConfirmationLine WHERE ConfirmationId=@batch", new { batch }));
        var deletion = new DeleteProcurementDraftLinesCommand { CommandId = Guid.NewGuid(), ExpectedVersion = cancelled.Data.Version, Reason = "释放选定需求", Lines = [cancelled.Data.Lines[0]] };
        var deleted = await db.InScopeAsync(p => p.GetRequiredService<IProcurementOrderReversalService>().DeleteLinesAsync(uid, deletion));
        Assert.IsTrue(deleted.Success, deleted.Message);
        Assert.HasCount(1, deleted.Data!.Lines);
        Assert.AreEqual(1, await db.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.PurchaseOrderLine WHERE PurchaseOrderId=@uid AND IsDeleted=1", new { uid }));
        var repeat = await db.InScopeAsync(p => p.GetRequiredService<IProcurementOrderReversalService>().DeleteLinesAsync(uid, deletion));
        Assert.IsTrue(repeat.Success, repeat.Message);
        Assert.AreEqual(deleted.Data.Version, repeat.Data!.Version);
        var last = await db.InScopeAsync(p => p.GetRequiredService<IProcurementOrderReversalService>().DeleteLinesAsync(uid, new() { CommandId = Guid.NewGuid(), ExpectedVersion = deleted.Data.Version, Reason = "不允许空单", Lines = deleted.Data.Lines }));
        Assert.IsFalse(last.Success);
        var newBasis = await db.ContextAsync(uid);
        Assert.IsTrue(newBasis.Success, newBasis.Message);
        var reconfirmed = await db.ConfirmAsync(uid, new() { CommandId = Guid.NewGuid(), ExpectedVersion = deleted.Data.Version, ContextFingerprint = newBasis.Data!.Fingerprint });
        Assert.IsTrue(reconfirmed.Success, reconfirmed.Message);
        Assert.AreNotEqual(batch, reconfirmed.Data!.ConfirmationId);
        var voided = await db.InScopeAsync(p => p.GetRequiredService<IProcurementOrderReversalService>().VoidAsync(uid, new() { CommandId = Guid.NewGuid(), ExpectedVersion = reconfirmed.Data.Version, Reason = "整单作废" }));
        Assert.IsTrue(voided.Success, voided.Message);
        Assert.AreEqual(reconfirmed.Data.ConfirmationId, voided.Data!.ConfirmationId);
        Assert.AreEqual(9, await db.ScalarAsync<int>("SELECT LifecycleStatus FROM dbo.PurchaseOrderHead WHERE Uid=@uid", new { uid }));
        var old = await db.CreateAsync(create);
        Assert.IsTrue(old.Success, old.Message);
        Assert.AreEqual(uid, old.Data!.PurchaseOrderId);
        Assert.AreEqual(9, await db.ScalarAsync<int>("SELECT LifecycleStatus FROM dbo.PurchaseOrderHead WHERE Uid=@uid", new { uid }));
    }

    /// <summary>日志拒绝写入时，采购状态及幂等回执必须随事务回滚。</summary>
    [TestMethod]
    public async Task LogFailureRollsBackHeadAndReceipt()
    {
        await using var db = new IndependentProcurementSqlFixture();
        await db.InitializeAsync();
        await db.PrepareReversalSchemaAsync();
        var created = await db.CreateAsync();
        Assert.IsTrue(created.Success, created.Message);
        var uid = created.Data!.PurchaseOrderId;
        await db.RejectLogsAsync();
        var command = new VoidProcurementOrderCommand { CommandId = Guid.NewGuid(), ExpectedVersion = created.Data.Version, Reason = "验证日志原子性" };
        var result = await db.InScopeAsync(p => p.GetRequiredService<IProcurementOrderReversalService>().VoidAsync(uid, command));
        Assert.IsFalse(result.Success);
        Assert.AreEqual(0, await db.ScalarAsync<int>("SELECT LifecycleStatus FROM dbo.PurchaseOrderHead WHERE Uid=@uid", new { uid }));
        Assert.AreEqual(created.Data.Version, await db.ScalarAsync<int>("SELECT Version FROM dbo.PurchaseOrderHead WHERE Uid=@uid", new { uid }));
        Assert.AreEqual(0, await db.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.ProcurementCommandReceipt WHERE CommandId=@CommandId", command));
    }
}
