using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Interfaces.Inventory.Posting;
using SwiftExport.Application.Services.Inventory.Posting;
using SwiftExport.Application.Interfaces.Inventory;
using SwiftExport.Application.Interfaces.Inventory.Warehouses;
using SwiftExport.Application.Interfaces.Procurement;
using SwiftExport.Contracts.SysEnums.Inventory;

namespace SwiftExport.Application.Tests.Procurement;

/// <summary>在唯一隔离库验证首次需求确认和仓库软删除；不连接业务数据库。</summary>
[TestClass, DoNotParallelize]
public sealed class ConfirmationDecouplingSqlTests
{
    /// <summary>将原测试身份附到单库例间完整事实导出。</summary>
    public TestContext TestContext { get; set; } = default!;
    /// <summary>首次需求采购没有历史快照也可确认，贡献不释放；旧预览不能覆盖引用变化。</summary>
    [TestMethod]
    public async Task FirstDemandConfirmationUsesReferencesWithoutHistoricalBom()
    {
        await using var db = new IndependentProcurementPostgreSqlFixture();
        await db.InitializeAsync(TestContext);
        var created = await db.CreateAsync();
        var uid = created.Data!.PurchaseOrderId;
        var line = created.Data.Lines[0].PurchaseOrderLineId;
        var source = Guid.NewGuid(); var net = Guid.NewGuid(); var link = Guid.NewGuid();
        await db.ExecuteAsync("""
            INSERT INTO public.gross_demand(uid,lifecycle_status,source_type,source_uid,source_no,source_line_id,product_id,is_non_standard,sku_snapshot,demand_qty)
            VALUES(@source,0,'Manual',gen_random_uuid(),'TEST-SOURCE',gen_random_uuid(),@ProductId,FALSE,'TEST-FG',10);
            INSERT INTO public.net_demand(uid,gross_demand_id,decomposition_revision,line_number,lifecycle_status,root_net_demand_id,delivery_mode,product_id,supply_type_snapshot,sku_snapshot,gross_qty,net_qty)
            VALUES(@net,@source,1,1,0,@net,0,@ProductId,'采购','TEST-FG',10,10);
            UPDATE public.purchase_order_line SET unlinked_qty=0,source_uid=@source WHERE uid=@line;
            INSERT INTO public.demand_execution_link(uid,net_demand_id,execution_request_id,command_item_id,target_type,target_head_id,target_line_id,target_no,link_status,allocated_qty,released_qty)
            VALUES(@link,@net,gen_random_uuid(),gen_random_uuid(),'PO',@uid,@line,'TEST-PO',0,10,0);
            """, new { source, net, link, uid, line, db.ProductId });
        var context = await db.ContextAsync(uid);
        Assert.IsTrue(context.Success, context.Message);
        await db.ExecuteAsync("UPDATE public.demand_execution_link SET version=version+1 WHERE uid=@link", new { link });
        var stale = await db.ConfirmAsync(uid, new() { CommandId=Guid.NewGuid(), ExpectedVersion=context.Data!.Version, ContextFingerprint=context.Data.Fingerprint });
        Assert.IsFalse(stale.Success);
        context = await db.ContextAsync(uid);
        var confirmed = await db.ConfirmAsync(uid, new() { CommandId=Guid.NewGuid(), ExpectedVersion=context.Data!.Version, ContextFingerprint=context.Data.Fingerprint });
        Assert.IsTrue(confirmed.Success, confirmed.Message);
        Assert.AreEqual(0m, await db.ScalarAsync<decimal>("SELECT released_qty FROM public.demand_execution_link WHERE uid=@link",new {link}));
        Assert.AreEqual(0, await db.ScalarAsync<int>("SELECT COUNT(*) FROM public.purchase_confirmation_node"));
    }

    /// <summary>有历史流水但零库存可软删除；普通入账和原仓反向均拒绝失效仓。</summary>
    [TestMethod]
    public async Task ZeroBalanceHistoryAllowsDeleteButPostingRequiresActiveWarehouse()
    {
        await using var db = new IndependentProcurementPostgreSqlFixture();
        await db.InitializeAsync(TestContext);
        async Task<SwiftExport.Contracts.Wrappers.ServiceResult<System.Collections.Generic.IReadOnlyList<SwiftExport.Contracts.DTOs.Inventory.InventoryPostingResultDto>>> Post(decimal qty) =>
            await db.InScopeAsync(p => p.GetRequiredService<ProcurementOrderReadSupport>().ReadConsistentAsync("Read", () =>
                p.GetRequiredService<IInventoryPostingService>().PostAsync(Guid.NewGuid(), [new InventoryPostingItem
                {
                    CommandItemId=Guid.NewGuid(), WarehouseSiteId=db.ReceiptSiteId, ProductId=db.ProductId,
                    QuantityDelta=qty, TransactionType=qty>0 ? InventoryTransactionTypeEnum.PurchaseReceiptInbound : InventoryTransactionTypeEnum.StockIssueOutbound,
                    SourceDocumentType="Test", SourceDocumentId=Guid.NewGuid(), SourceLineId=Guid.NewGuid(), OccurredDate=DateTime.Now
                }]), default));
        var incoming = await Post(10);
        Assert.IsTrue(incoming.Success, incoming.Message);
        var blocked = await db.InScopeAsync(p => p.GetRequiredService<IWarehouseManagementService>().DeleteWarehouseSiteAsync(db.ReceiptSiteId,
            new() {CommandId=Guid.NewGuid(),ExpectedVersion=1,Reason="非零库存不得删除"}));
        Assert.IsFalse(blocked.Success);
        var outgoing = await Post(-10);
        Assert.IsTrue(outgoing.Success, outgoing.Message);
        var deleted = await db.InScopeAsync(p => p.GetRequiredService<IWarehouseManagementService>().DeleteWarehouseSiteAsync(db.ReceiptSiteId,
            new() {CommandId=Guid.NewGuid(),ExpectedVersion=1,Reason="历史保留软删除"}));
        Assert.IsTrue(deleted.Success, deleted.Message);
        Assert.AreEqual(2,await db.ScalarAsync<int>("SELECT COUNT(*) FROM public.inventory_ledger_entry"));
        Assert.IsFalse((await Post(1)).Success);
        var reversed = await db.InScopeAsync(p => p.GetRequiredService<ProcurementOrderReadSupport>().ReadConsistentAsync("Read", () =>
            p.GetRequiredService<IInventoryPostingService>().ReverseAsync(Guid.NewGuid(), [new InventoryReverseItem
            {CommandItemId=Guid.NewGuid(),OriginalLedgerId=outgoing.Data![0].LedgerId,SourceDocumentType="Test",SourceDocumentId=Guid.NewGuid(),SourceLineId=Guid.NewGuid(),Reason="验证原仓",OccurredDate=DateTime.Now}]), default));
        Assert.IsFalse(reversed.Success);
        // 外层采购测试事务隐藏库存异常；下面直接捕获库存内核的明确原仓提示。
        var message = await db.InScopeAsync(async p =>
        {
            var session=p.GetRequiredService<SwiftExport.Infrastructure.Data.DbSession>();
            using var tx=session.Connection.BeginTransaction(); session.Transaction=tx;
            try { await p.GetRequiredService<IInventoryPostingService>().ReverseAsync(Guid.NewGuid(), [new InventoryReverseItem
                {CommandItemId=Guid.NewGuid(),OriginalLedgerId=outgoing.Data![0].LedgerId,SourceDocumentType="Test",SourceDocumentId=Guid.NewGuid(),SourceLineId=Guid.NewGuid(),Reason="原仓校验",OccurredDate=DateTime.Now}]); return ""; }
            catch (SwiftExport.Application.Services.Inventory.Posting.InventoryPostingException ex) { return ex.Message; }
            finally { session.Transaction=null; }
        });
        StringAssert.Contains(message,"恢复原仓库");
        Assert.AreEqual(0m,await db.ScalarAsync<decimal>("SELECT on_hand_qty FROM public.inventory_balance"));
    }
    /// <summary>删除必须等库存事务释放同一仓库行锁，再复核已提交的非零余额。</summary>
    [TestMethod]
    public async Task WarehouseDeletionWaitsForPostingAndRechecksBalance()
    {
        await using var db = new IndependentProcurementPostgreSqlFixture();
        await db.InitializeAsync(TestContext);
        await db.InScopeAsync(async p =>
        {
            var session=p.GetRequiredService<SwiftExport.Infrastructure.Data.DbSession>();
            using var tx=session.Connection.BeginTransaction(); session.Transaction=tx;
            var warehouse=p.GetRequiredService<SwiftExport.Core.Interfaces.Inventory.Warehouses.IWarehouseManagementRepository>();
            await warehouse.GetForUpdateAsync(db.ReceiptSiteId);
            var reader=await Dapper.SqlMapper.ExecuteScalarAsync<int>(session.Connection,"SELECT pg_backend_pid()",transaction:tx);
            var deletion=db.InScopeAsync(other => other.GetRequiredService<IWarehouseManagementService>().DeleteWarehouseSiteAsync(db.ReceiptSiteId,
                new() {CommandId=Guid.NewGuid(),ExpectedVersion=1,Reason="并发删除"}));
            try
            {
                var blocked=0;
                for(var i=0;i<30 && blocked==0;i++)
                {
                    await Task.Delay(100);
                    blocked=await db.ScalarAsync<int>("SELECT COUNT(*) FROM pg_stat_activity WHERE @reader=ANY(pg_blocking_pids(pid)) AND datname=current_database()",new {reader});
                }
                Assert.IsTrue(blocked>0,"删除必须被仓库行锁阻塞。");
                await p.GetRequiredService<IInventoryPostingService>().PostAsync(Guid.NewGuid(),[new InventoryPostingItem
                {CommandItemId=Guid.NewGuid(),WarehouseSiteId=db.ReceiptSiteId,ProductId=db.ProductId,QuantityDelta=1,
                    TransactionType=InventoryTransactionTypeEnum.PurchaseReceiptInbound,SourceDocumentType="Test",
                    SourceDocumentId=Guid.NewGuid(),SourceLineId=Guid.NewGuid(),OccurredDate=DateTime.Now}]);
                tx.Commit(); session.Transaction=null;
                var result=await deletion;
                Assert.IsFalse(result.Success);
                StringAssert.Contains(result.Message,"库存余额非零");
                return true;
            }
            finally { if(session.Transaction is not null) { tx.Rollback(); session.Transaction=null; } }
        });
        Assert.AreEqual(0,await db.ScalarAsync<int>("SELECT is_deleted::integer FROM public.warehouse_site WHERE uid=@Uid",new {Uid=db.ReceiptSiteId}));
    }

}
