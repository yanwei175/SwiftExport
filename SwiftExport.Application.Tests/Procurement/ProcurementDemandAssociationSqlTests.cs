using System.Data;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Commands;
using SwiftExport.Application.Interfaces.Procurement;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Core.Interfaces.DemandPlanning.Materials.Execution;
using SwiftExport.Core.Interfaces.Procurement;
using SwiftExport.Infrastructure.Data;

namespace SwiftExport.Application.Tests.Procurement;

/// <summary>获批单PG隔离库 验证追加关联的守恒、收货解耦、幂等、并发和完整回滚；不连接业务数据库。</summary>
[TestClass, DoNotParallelize]
public sealed class ProcurementDemandAssociationSqlTests
{
    /// <summary>将原测试身份附到单库例间完整事实导出。</summary>
    public TestContext TestContext { get; set; } = default!;
    /// <summary>真实 SQL 必须显式启用，不能把跳过当作通过。</summary>
    [TestInitialize]
    public void RequireSql() { if (Environment.GetEnvironmentVariable("SWIFTEXPORT_RUN_PG_LEGACY_TESTS")!="1") Assert.Inconclusive("需启用已批准单库PG测试。"); }

    /// <summary>同一行同一物料两次分配，仅新增 Link；原创建和旧关联命令重放不复活已取消贡献。</summary>
    [TestMethod]
    public async Task RepeatedAssociationUsesRemainingQuantityAndReplaysOriginalResult()
    {
        await using var db=new IndependentProcurementPostgreSqlFixture(); await db.InitializeAsync(TestContext);
        var originalCreate=db.CreateCommand();
        var created=await db.CreateAsync(originalCreate); Assert.IsTrue(created.Success,created.Message);
        var uid=created.Data!.PurchaseOrderId; var line=created.Data.Lines.Single().PurchaseOrderLineId;
        var demand=await SeedDemand(db);
        var first=await Command(db,uid,line,demand,6m);
        var preview=await Preview(db,uid,first);
        Assert.AreEqual(4m,preview.Items.Single().LineUnlinkedAfter);
        Assert.AreEqual(0,await db.ScalarAsync<int>("SELECT COUNT(*) FROM public.demand_execution_link"));
        Assert.AreEqual(10m,await db.ScalarAsync<decimal>("SELECT unlinked_qty FROM public.purchase_order_line WHERE uid=@line",new {line}));
        first.PreviewToken=preview.Token;
        var applied=await Associate(db,uid,first); Assert.IsTrue(applied.Success,applied.Message);
        var partialTrace=await db.InScopeAsync(p => p.GetRequiredService<IPurchaseOrderTraceService>().GetAsync(uid));
        Assert.IsTrue(partialTrace.Success,partialTrace.Message);
        Assert.AreEqual(2,partialTrace.Data!.OriginKind);
        Assert.AreEqual(4m,partialTrace.Data.Lines.Single().UnlinkedQty);
        Assert.AreEqual(6m,partialTrace.Data.Lines.Single().Links.Single().EffectiveQty);
        Assert.IsTrue(string.IsNullOrEmpty(partialTrace.Data.Lines.Single().ReviewReason), "独立采购补关联是合法状态，不应提示来源类别异常。");
        Assert.IsFalse(partialTrace.Data.Lines.Single().Links.Single().NeedsReview);
        var second=await Command(db,uid,line,demand,4m);
        second.PreviewToken=(await Preview(db,uid,second)).Token;
        Assert.IsTrue((await Associate(db,uid,second)).Success);
        Assert.AreEqual(2,await db.ScalarAsync<int>("SELECT COUNT(*) FROM public.demand_execution_link"));
        Assert.AreEqual(0m,await db.ScalarAsync<decimal>("SELECT unlinked_qty FROM public.purchase_order_line WHERE uid=@line",new {line}));
        Assert.AreEqual(10m,await db.ScalarAsync<decimal>("SELECT SUM(allocated_qty-released_qty) FROM public.demand_execution_link"));
        Assert.AreEqual(2,await db.ScalarAsync<int>("SELECT origin_kind FROM public.purchase_order_head WHERE uid=@uid",new {uid}));
        var context=await db.ContextAsync(uid); Assert.IsTrue(context.Success,context.Message);
        var confirmed=await db.ConfirmAsync(uid,new() {CommandId=Guid.NewGuid(),ExpectedVersion=context.Data!.Version,ContextFingerprint=context.Data.Fingerprint});
        Assert.IsTrue(confirmed.Success,confirmed.Message);
        var voided=await db.InScopeAsync(p => p.GetRequiredService<IProcurementOrderReversalService>().VoidAsync(uid,
            new() {CommandId=Guid.NewGuid(),ExpectedVersion=confirmed.Data!.Version,Reason="验证旧命令不会复活引用"}));
        Assert.IsTrue(voided.Success,voided.Message);
        Assert.AreEqual(0m,await db.ScalarAsync<decimal>("SELECT SUM(allocated_qty-released_qty) FROM public.demand_execution_link"));
        var history=await db.InScopeAsync(p => p.GetRequiredService<IPurchaseOrderTraceService>().GetAsync(uid));
        Assert.IsTrue(history.Success,history.Message);
        Assert.AreEqual(2,history.Data!.OriginKind);
        Assert.AreEqual(2,history.Data.Lines.Single().Links.Count);
        Assert.IsTrue(history.Data.Lines.Single().Links.All(x => x.EffectiveQty==0 && x.LinkStatus==DemandExecutionLinkStatusEnum.Cancelled));
        Assert.IsTrue(string.IsNullOrEmpty(history.Data.Lines.Single().ReviewReason));
        var replay=await Associate(db,uid,first); Assert.IsTrue(replay.Success,replay.Message);
        Assert.AreEqual(applied.Data!.Version,replay.Data!.Version);
        Assert.AreEqual(0m,await db.ScalarAsync<decimal>("SELECT SUM(allocated_qty-released_qty) FROM public.demand_execution_link"));
        var createReplay=await db.CreateAsync(originalCreate);
        Assert.IsTrue(createReplay.Success,createReplay.Message);
        Assert.AreEqual(created.Data.Version,createReplay.Data!.Version);
        Assert.AreEqual(0m,await db.ScalarAsync<decimal>("SELECT SUM(allocated_qty-released_qty) FROM public.demand_execution_link"));
        var excessive=await Command(db,uid,line,demand,1m);
        Assert.IsFalse((await db.InScopeAsync(p => p.GetRequiredService<IProcurementDemandAssociationService>().PreviewAsync(uid,
            new() {ExpectedVersion=excessive.ExpectedVersion,Items=excessive.Items}))).Success);
    }

    /// <summary>已确认采购有正式收货、有效收货草稿和历史出货时可补关联；确认批次、实际库存和出货记录保持不变。</summary>
    [TestMethod]
    public async Task ConfirmedReceiptAndShipmentHistoryDoNotBlockAssociation()
    {
        await using var db=new IndependentProcurementPostgreSqlFixture(); await db.InitializeAsync(TestContext);
        var created=await db.CreateAsync(); var uid=created.Data!.PurchaseOrderId; var line=created.Data.Lines.Single().PurchaseOrderLineId;
        var basis=await db.ContextAsync(uid);
        var confirmed=await db.ConfirmAsync(uid,new() {CommandId=Guid.NewGuid(),ExpectedVersion=basis.Data!.Version,ContextFingerprint=basis.Data.Fingerprint});
        Assert.IsTrue(confirmed.Success,confirmed.Message);
        async Task<SwiftExport.Contracts.DTOs.Inventory.Inbounds.Draft.InboundDraftCommandResult> Receipt(decimal qty)
        {
            var r=await db.InScopeAsync(p => p.GetRequiredService<SwiftExport.Application.Interfaces.Inventory.Inbounds.IInboundDraftService>().CreateAsync(
                new() {CommandId=Guid.NewGuid(),Draft=new() {DocumentType=SwiftExport.Contracts.SysEnums.Inventory.InboundDocumentTypeEnum.PurchaseReceipt,WarehouseSiteId=db.ReceiptSiteId,ReceiptDate=DateTime.Today,
                    Lines=[new() {SourceLineId=line,Quantity=qty}]} }));
            Assert.IsTrue(r.Success,r.Message); return r.Data!;
        }
        var receipt=await Receipt(4m);
        var posted=await db.InScopeAsync(p => p.GetRequiredService<SwiftExport.Application.Interfaces.Inventory.Inbounds.IInboundPostingService>()
            .PostAsync(receipt.Uid,new() {CommandId=Guid.NewGuid(),ExpectedVersion=receipt.Version}));
        Assert.IsTrue(posted.Success,posted.Message);
        _=await Receipt(2m);
        var demand=await SeedDemand(db); var packing=Guid.NewGuid();
        // 模拟既有未关联的历史正式出货；补关联负责追溯关系，不补造原出货的执行证据或流水。
        await db.ExecuteAsync("""
            INSERT INTO public.packing_list_head(uid,packing_list_no,customer_id,invoice_date,lifecycle_status,posting_status,create_command_id,post_command_id)
            VALUES(@packing,'PL-HISTORY',@CustomerId,LOCALTIMESTAMP,0,1,gen_random_uuid(),gen_random_uuid());
            INSERT INTO public.packing_list_line(uid,packing_list_head_id,line_number,gross_demand_id,product_id,is_non_standard_snapshot,source_type_snapshot,
                source_document_no_snapshot,sku_snapshot,unit_snapshot,qty)
            VALUES(gen_random_uuid(),@packing,1,@gross,@ProductId,FALSE,'MD','SOURCE','TEST-FG','PCS',7);
            """,new {packing,db.CustomerId,db.ProductId,gross=demand.Gross});
        var ledgers=await db.ScalarAsync<int>("SELECT COUNT(*) FROM public.inventory_ledger_entry");
        var command=await Command(db,uid,line,demand,6m);
        command.PreviewToken=(await Preview(db,uid,command)).Token;
        var result=await Associate(db,uid,command); Assert.IsTrue(result.Success,result.Message);
        Assert.AreEqual(ConfirmationStatusEnum.已确认,result.Data!.ConfirmationStatus);
        Assert.AreEqual(confirmed.Data!.ConfirmationId,result.Data.ConfirmationId);
        Assert.AreEqual(ledgers,await db.ScalarAsync<int>("SELECT COUNT(*) FROM public.inventory_ledger_entry"));
        Assert.AreEqual(4m,await db.ScalarAsync<decimal>("SELECT on_hand_qty FROM public.inventory_balance"));
        Assert.AreEqual(7m,await db.ScalarAsync<decimal>("SELECT qty FROM public.packing_list_line WHERE packing_list_head_id=@packing",new {packing}));
        Assert.AreEqual(1,await db.ScalarAsync<int>("SELECT posting_status FROM public.packing_list_head WHERE uid=@packing",new {packing}));
        Assert.AreEqual(10m,await db.ScalarAsync<decimal>("SELECT quantity FROM public.purchase_order_line WHERE uid=@line",new {line}));
        Assert.AreEqual(25m,await db.ScalarAsync<decimal>("SELECT tax_amount FROM public.purchase_order_line WHERE uid=@line",new {line}));
        var draft=Guid.NewGuid();
        await db.ExecuteAsync("""
            INSERT INTO public.packing_list_head(uid,packing_list_no,customer_id,invoice_date,lifecycle_status,posting_status,create_command_id)
            VALUES(@draft,'PL-PREVIEW',@CustomerId,LOCALTIMESTAMP,0,0,gen_random_uuid());
            INSERT INTO public.packing_list_line(uid,packing_list_head_id,line_number,gross_demand_id,product_id,is_non_standard_snapshot,
                source_type_snapshot,source_document_no_snapshot,sku_snapshot,unit_snapshot,qty)
            VALUES(gen_random_uuid(),@draft,1,@gross,@ProductId,FALSE,'MD','SOURCE','TEST-FG','PCS',1);
            """,new {draft,db.CustomerId,db.ProductId,gross=demand.Gross});
        async Task<SwiftExport.Contracts.Wrappers.ServiceResult<SwiftExport.Contracts.DTOs.Shipping.PackingLists.PackingListPostingPreviewDto>> PackingPreview()
            => await db.InScopeAsync(p => p.GetRequiredService<SwiftExport.Application.Interfaces.Shipping.IPackingListPostingService>()
                .PreviewAsync(draft,new() {ExpectedVersion=1,BusinessDate=DateTime.Today},default));
        var oldPreview=await PackingPreview(); Assert.IsTrue(oldPreview.Success,oldPreview.Message);
        Assert.AreEqual(0,oldPreview.Data!.Blockers.Count); Assert.IsNotNull(oldPreview.Data.PreviewToken);
        var extra=await Command(db,uid,line,demand,2m); extra.PreviewToken=(await Preview(db,uid,extra)).Token;
        Assert.IsTrue((await Associate(db,uid,extra)).Success);
        var newPreview=await PackingPreview(); Assert.IsTrue(newPreview.Success,newPreview.Message);
        Assert.AreEqual(0,newPreview.Data!.Blockers.Count); Assert.AreNotEqual(oldPreview.Data.PreviewToken,newPreview.Data.PreviewToken);
        var stalePost=await db.InScopeAsync(p => p.GetRequiredService<SwiftExport.Application.Interfaces.Shipping.IPackingListPostingService>()
            .PostAsync(draft,new() {CommandId=Guid.NewGuid(),ExpectedVersion=1,BusinessDate=DateTime.Today,PreviewToken=oldPreview.Data.PreviewToken!},default));
        Assert.IsFalse(stalePost.Success); Assert.AreEqual("PREVIEW_STALE",stalePost.Error!.Code);
        Assert.AreEqual(ledgers,await db.ScalarAsync<int>("SELECT COUNT(*) FROM public.inventory_ledger_entry"));
    }

    /// <summary>非标独立采购无需 ProductId 可确认；人工关联需明确接受，取消后重新确认仍不生成 BOM 快照。</summary>
    [TestMethod]
    public async Task NonStandardConfirmationAndExplicitManualAssociation()
    {
        await using var db=new IndependentProcurementPostgreSqlFixture(); await db.InitializeAsync(TestContext);
        var create=db.CreateCommand(); create.Lines=[new() {IsNonStandard=true,DescriptionZh="非标总成",Unit="PCS",Quantity=10,UnitPrice=2,TaxPrice=2.5m}];
        var created=await db.CreateAsync(create); Assert.IsTrue(created.Success,created.Message);
        var uid=created.Data!.PurchaseOrderId; var line=created.Data.Lines.Single().PurchaseOrderLineId;
        var basis=await db.ContextAsync(uid); Assert.IsTrue(basis.Success,basis.Message);
        var confirmed=await db.ConfirmAsync(uid,new() {CommandId=Guid.NewGuid(),ExpectedVersion=basis.Data!.Version,ContextFingerprint=basis.Data.Fingerprint});
        Assert.IsTrue(confirmed.Success,confirmed.Message);
        var demand=await SeedDemand(db,nonStandard:true);
        var command=await Command(db,uid,line,demand,6m);
        var refused=await db.InScopeAsync(p => p.GetRequiredService<IProcurementDemandAssociationService>().PreviewAsync(uid,
            new() {ExpectedVersion=command.ExpectedVersion,Items=command.Items}));
        Assert.IsFalse(refused.Success);
        command.Items[0].AcceptNonStandardMatch=true;
        command.PreviewToken=(await Preview(db,uid,command)).Token;
        var result=await Associate(db,uid,command); Assert.IsTrue(result.Success,result.Message);
        var cancel=await db.InScopeAsync(p => p.GetRequiredService<IProcurementOrderReversalService>().CancelConfirmationAsync(uid,
            new() {CommandId=Guid.NewGuid(),ExpectedVersion=result.Data!.Version,Reason="非标重新确认"}));
        Assert.IsTrue(cancel.Success,cancel.Message);
        basis=await db.ContextAsync(uid); Assert.IsTrue(basis.Success,basis.Message);
        var again=await db.ConfirmAsync(uid,new() {CommandId=Guid.NewGuid(),ExpectedVersion=basis.Data!.Version,ContextFingerprint=basis.Data.Fingerprint});
        Assert.IsTrue(again.Success,again.Message);
        Assert.AreNotEqual(confirmed.Data!.ConfirmationId,again.Data!.ConfirmationId);
        Assert.AreEqual(0m,await db.ScalarAsync<decimal>("SELECT released_qty FROM public.demand_execution_link"));
    }

    /// <summary>并发采购争用同一物料时最多一个命令承接，不能超量或留下失败回执。</summary>
    [TestMethod]
    public async Task ConcurrentOrdersCannotOverAllocateMaterial()
    {
        await using var db=new IndependentProcurementPostgreSqlFixture(); await db.InitializeAsync(TestContext);
        var one=await db.CreateAsync(); var two=await db.CreateAsync(); var demand=await SeedDemand(db);
        var a=await Command(db,one.Data!.PurchaseOrderId,one.Data.Lines.Single().PurchaseOrderLineId,demand,7m);
        var b=await Command(db,two.Data!.PurchaseOrderId,two.Data.Lines.Single().PurchaseOrderLineId,demand,7m);
        a.PreviewToken=(await Preview(db,one.Data.PurchaseOrderId,a)).Token;
        b.PreviewToken=(await Preview(db,two.Data.PurchaseOrderId,b)).Token;
        var results=await Task.WhenAll(Associate(db,one.Data.PurchaseOrderId,a),Associate(db,two.Data.PurchaseOrderId,b));
        Assert.AreEqual(1,results.Count(x => x.Success));
        Assert.IsTrue(results.Single(x => !x.Success).Error!.Code is ServiceErrorCodes.BusinessBlocked or ServiceErrorCodes.VersionConflict);
        Assert.AreEqual(7m,await db.ScalarAsync<decimal>("SELECT SUM(allocated_qty-released_qty) FROM public.demand_execution_link"));
        Assert.AreEqual(1,await db.ScalarAsync<int>("SELECT COUNT(*) FROM public.business_operation_log WHERE operation_type='AssociateDemands'"));
    }

    /// <summary>预览后引用版本变化、跨来源、单位未知及命令内容冲突均拒绝，失败不写引用。</summary>
    [TestMethod]
    public async Task SourceUnitVersionAndCommandIdentityRemainGuarded()
    {
        await using var db=new IndependentProcurementPostgreSqlFixture(); await db.InitializeAsync(TestContext);
        var created=await db.CreateAsync(); var uid=created.Data!.PurchaseOrderId; var line=created.Data.Lines.Single().PurchaseOrderLineId;
        var demand=await SeedDemand(db); var command=await Command(db,uid,line,demand,6m);
        command.PreviewToken=(await Preview(db,uid,command)).Token;
        await db.ExecuteAsync("UPDATE public.net_demand SET unit_snapshot=NULL WHERE uid=@uid",new {uid=demand.Net});
        Assert.IsFalse((await Associate(db,uid,command)).Success);
        await db.ExecuteAsync("UPDATE public.net_demand SET unit_snapshot='PCS',version=version+1 WHERE uid=@uid",new {uid=demand.Net});
        var stale=await Associate(db,uid,command); Assert.IsFalse(stale.Success); Assert.AreEqual(ServiceErrorCodes.VersionConflict,stale.Error!.Code);
        command=await Command(db,uid,line,demand,6m); command.PreviewToken=(await Preview(db,uid,command)).Token;
        var applied=await Associate(db,uid,command); Assert.IsTrue(applied.Success,applied.Message);
        command.Reason="换内容不能复用命令身份";
        var conflict=await Associate(db,uid,command); Assert.IsFalse(conflict.Success); Assert.AreEqual(ServiceErrorCodes.CommandConflict,conflict.Error!.Code);
        var another=await SeedDemand(db);
        var crossSource=await Command(db,uid,line,another,1m);
        Assert.IsFalse((await db.InScopeAsync(p => p.GetRequiredService<IProcurementDemandAssociationService>().PreviewAsync(uid,
            new() {ExpectedVersion=crossSource.ExpectedVersion,Items=crossSource.Items}))).Success);
        var reused=await Command(db,uid,line,demand,1m);
        reused.Items[0].CommandItemId=command.Items[0].CommandItemId;
        reused.PreviewToken=(await Preview(db,uid,reused)).Token;
        var reusedResult=await Associate(db,uid,reused);
        Assert.IsFalse(reusedResult.Success); Assert.AreEqual(ServiceErrorCodes.CommandConflict,reusedResult.Error!.Code);
        Assert.AreEqual(1,await db.ScalarAsync<int>("SELECT COUNT(*) FROM public.demand_execution_link"));
        // 唯一命令身份永久占用，历史已取消引用不能借新关联复用；实体禁止删除 Link。
        var oldItem=Guid.NewGuid();
        await db.ExecuteAsync("""
            INSERT INTO public.demand_execution_link(uid,is_deleted,net_demand_id,execution_request_id,command_item_id,target_type,target_head_id,
                target_no,target_line_id,link_status,allocated_qty,released_qty)
            VALUES(gen_random_uuid(),FALSE,@net,gen_random_uuid(),@oldItem,'PO',gen_random_uuid(),'OLD-CANCELLED',gen_random_uuid(),8,1,1);
            """,new {net=demand.Net,oldItem});
        var cancelledIdentity=await Command(db,uid,line,demand,1); cancelledIdentity.Items[0].CommandItemId=oldItem;
        cancelledIdentity.PreviewToken=(await Preview(db,uid,cancelledIdentity)).Token;
        var cancelledResult=await Associate(db,uid,cancelledIdentity);
        Assert.IsFalse(cancelledResult.Success); Assert.AreEqual(ServiceErrorCodes.CommandConflict,cancelledResult.Error!.Code);
        Assert.AreEqual(6m,await db.ScalarAsync<decimal>("SELECT SUM(allocated_qty-released_qty) FROM public.demand_execution_link"));
    }

    /// <summary>日志真实失败回滚新增引用、未关联量和头行版本，原命令随后可安全重试。</summary>
    [TestMethod]
    public async Task LogFailureRollsBackAllAssociationWrites()
    {
        await using var db=new IndependentProcurementPostgreSqlFixture(); await db.InitializeAsync(TestContext);
        var created=await db.CreateAsync(); var uid=created.Data!.PurchaseOrderId; var line=created.Data.Lines.Single().PurchaseOrderLineId;
        var demand=await SeedDemand(db); var command=await Command(db,uid,line,demand,6m); command.PreviewToken=(await Preview(db,uid,command)).Token;
        await db.RejectLogsAsync();
        var failed=await Associate(db,uid,command); Assert.IsFalse(failed.Success);
        Assert.AreEqual(10m,await db.ScalarAsync<decimal>("SELECT unlinked_qty FROM public.purchase_order_line WHERE uid=@line",new {line}));
        Assert.AreEqual(1,await db.ScalarAsync<int>("SELECT version FROM public.purchase_order_head WHERE uid=@uid",new {uid}));
        Assert.AreEqual(0,await db.ScalarAsync<int>("SELECT COUNT(*) FROM public.demand_execution_link"));
        Assert.AreEqual(0,await db.ScalarAsync<int>("SELECT COUNT(*) FROM public.command_receipt WHERE command_id=@id",new {id=command.CommandId}));
        await db.ExecuteAsync("DROP TRIGGER tr_test_reject_log ON public.business_operation_log; DROP FUNCTION public.p09_test_reject_log();");
        Assert.IsTrue((await Associate(db,uid,command)).Success);
        Assert.IsTrue(await db.InScopeAsync(p => Task.FromResult(p.GetRequiredService<DbSession>().Transaction is null)));
    }

    /// <summary>仓储后续行冲突时停止继续写入，公共执行器回滚已成功的头及前序行。</summary>
    [TestMethod]
    public async Task LaterLineConflictRollsBackEarlierUpdates()
    {
        await using var db=new IndependentProcurementPostgreSqlFixture(); await db.InitializeAsync(TestContext);
        var create=db.CreateCommand(); create.Lines.Add(new() {ProductId=db.ProductId,Quantity=3,UnitPrice=2,TaxPrice=2.5m});
        var created=await db.CreateAsync(create); var uid=created.Data!.PurchaseOrderId;
        var failed=await db.InScopeAsync(p => p.GetRequiredService<ProcurementOrderCommandSupport>().RunAsync(async () =>
            await p.GetRequiredService<BusinessCommandExecutor>().ExecuteAsync(Guid.NewGuid(),"AssociationRollbackTest",uid,new {},async ct =>
            {
                var facts=ProcurementOrderCommandSupport.RequireLockedFacts(await p.GetRequiredService<IProcurementOrderFactLockRepository>().LockForAssociationAsync(uid,[],ct));
                var ordered=facts.Lines.OrderBy(x => x.Uid).ToArray();
                ordered[0].UnlinkedQty-=1; ordered[1].UnlinkedQty-=1; ordered[1].Version++;
                var result=await p.GetRequiredService<IProcurementDemandAssociationRepository>().ApplyAsync(facts.Head!,ordered,ct);
                Assert.IsFalse(result.Success);
                Assert.AreEqual(2,await p.GetRequiredService<DbSession>().Connection.ExecuteScalarAsync<int>(
                    "SELECT version FROM public.purchase_order_line WHERE uid=@Uid",new {ordered[0].Uid},p.GetRequiredService<DbSession>().Transaction));
                ProcurementOrderCommandSupport.EnsureWriteSucceeded(result);
                return true;
            },default,IsolationLevel.Serializable),default));
        Assert.IsFalse(failed.Success); Assert.AreEqual(ServiceErrorCodes.VersionConflict,failed.Error!.Code);
        Assert.AreEqual(13m,await db.ScalarAsync<decimal>("SELECT SUM(unlinked_qty) FROM public.purchase_order_line WHERE purchase_order_id=@uid",new {uid}));
        Assert.AreEqual(2,await db.ScalarAsync<int>("SELECT COUNT(*) FROM public.purchase_order_line WHERE purchase_order_id=@uid AND version=1",new {uid}));
    }

    /// <summary>同单多来源行共同分配按整组守恒；查询显示实时剩余、完整贡献，并沿用需求回退阻断。</summary>
    [TestMethod]
    public async Task CandidateQueriesAndMultipleContributionsUseCurrentRemainingQuantity()
    {
        await using var db=new IndependentProcurementPostgreSqlFixture(); await db.InitializeAsync(TestContext);
        var created=await db.CreateAsync(); var uid=created.Data!.PurchaseOrderId; var line=created.Data.Lines.Single().PurchaseOrderLineId;
        var source=Guid.NewGuid(); var one=await SeedDemand(db,source); var two=await SeedDemand(db,source);
        async Task<SwiftExport.Contracts.Wrappers.ServiceResult<SwiftExport.Contracts.Wrappers.PagedResponseBase<ProcurementDemandCandidateDto>>> Search(int page=1,string? keyword=null)
            => await db.InScopeAsync(p => p.GetRequiredService<IProcurementDemandAssociationService>().SearchCandidatesAsync(uid,
                new() {PurchaseOrderLineId=line,SourceUid=source,SourceType="MD",PageIndex=page,PageSize=1,Keyword=keyword}));
        var candidates=await Search(); Assert.IsTrue(candidates.Success,candidates.Message);
        Assert.AreEqual(2,candidates.Data!.TotalCount); Assert.AreEqual(1,candidates.Data.Items.Count);
        Assert.AreEqual("PCS",candidates.Data.Items.Single().Unit);
        Assert.AreNotEqual(candidates.Data.Items.Single().NetDemandId,(await Search(2)).Data!.Items.Single().NetDemandId);
        Assert.AreEqual(0,(await Search(keyword:"%" )).Data!.TotalCount); // 通配符作为普通输入转义。
        var command=await Command(db,uid,line,one,6);
        command.Items.Add((await Command(db,uid,line,two,5)).Items.Single());
        Assert.IsFalse((await db.InScopeAsync(p => p.GetRequiredService<IProcurementDemandAssociationService>().PreviewAsync(uid,
            new() {ExpectedVersion=command.ExpectedVersion,Items=command.Items}))).Success);
        command.Items[1].Quantity=3;
        var preview=await Preview(db,uid,command); Assert.IsTrue(preview.Items.All(x => x.LineUnlinkedAfter==1));
        command.PreviewToken=preview.Token; Assert.IsTrue((await Associate(db,uid,command)).Success);
        var context=await db.InScopeAsync(p => p.GetRequiredService<IProcurementDemandAssociationService>().GetContextAsync(uid));
        Assert.AreEqual(1m,context.Data!.Lines.Single().UnlinkedQty);
        Assert.AreEqual(9m,context.Data.Lines.Single().LinkedQty); Assert.AreEqual(2,context.Data.Lines.Single().Links.Count);
        Assert.IsNull(await db.ScalarAsync<Guid?>("SELECT source_line_id FROM public.purchase_order_line WHERE uid=@line",new {line}));
        var after=await db.InScopeAsync(p => p.GetRequiredService<IProcurementDemandAssociationService>().SearchCandidatesAsync(uid,
            new() {PurchaseOrderLineId=line,SourceUid=source,PageSize=10}));
        Assert.AreEqual(4m,after.Data!.Items.Single(x => x.NetDemandId==one.Net).RemainingQty);
        Assert.AreEqual(7m,after.Data.Items.Single(x => x.NetDemandId==two.Net).RemainingQty);
        var blockers=await db.InScopeAsync(p => p.GetRequiredService<INetDemandExecutionLinkRepository>().GetRollbackBlockersAsync([one.Net,two.Net]));
        Assert.AreEqual(2,blockers.Count);
        var voided=await db.InScopeAsync(p => p.GetRequiredService<IProcurementOrderReversalService>().VoidAsync(uid,
            new() {CommandId=Guid.NewGuid(),ExpectedVersion=context.Data.Version,Reason="解除全部来源贡献"}));
        Assert.IsTrue(voided.Success,voided.Message);
        Assert.AreEqual(0,(await db.InScopeAsync(p => p.GetRequiredService<INetDemandExecutionLinkRepository>().GetRollbackBlockersAsync([one.Net,two.Net]))).Count);
    }

    /// <summary>读取及提交先检查编辑权限，不能通过历史命令回执绕过授权。</summary>
    [TestMethod]
    public async Task AssociationRequiresEditPermissionBeforeReadingOrReplaying()
    {
        await using var db=new IndependentProcurementPostgreSqlFixture(); await db.InitializeAsync(TestContext);
        var created=await db.CreateAsync(); var uid=created.Data!.PurchaseOrderId; var line=created.Data.Lines.Single().PurchaseOrderLineId;
        var demand=await SeedDemand(db); var command=await Command(db,uid,line,demand,6);
        command.PreviewToken=(await Preview(db,uid,command)).Token; Assert.IsTrue((await Associate(db,uid,command)).Success);
        await db.ExecuteAsync("DELETE FROM public.sys_user_role WHERE user_id=@uid",new {uid=db.OtherActorId});
        db.ActorId=db.OtherActorId;
        var context=await db.InScopeAsync(p => p.GetRequiredService<IProcurementDemandAssociationService>().GetContextAsync(uid));
        Assert.IsFalse(context.Success); Assert.AreEqual(ServiceErrorCodes.Forbidden,context.Error!.Code);
        var replay=await Associate(db,uid,command);
        Assert.IsFalse(replay.Success); Assert.AreEqual(ServiceErrorCodes.Forbidden,replay.Error!.Code);
        Assert.AreEqual(1,await db.ScalarAsync<int>("SELECT COUNT(*) FROM public.demand_execution_link"));
    }

    /// <summary>客供件人工关联沿父级实际采购供应商检查送达，允许不同供货供应商，不绑定收货或消耗仓。</summary>
    [TestMethod]
    public async Task CustomerSuppliedAssociationChecksActualParentDeliverySupplier()
    {
        await using var db=new IndependentProcurementPostgreSqlFixture(); await db.InitializeAsync(TestContext);
        var parent=await db.CreateAsync(); var demand=await SeedDemand(db);
        var parentCommand=await Command(db,parent.Data!.PurchaseOrderId,parent.Data.Lines.Single().PurchaseOrderLineId,demand,10);
        parentCommand.PreviewToken=(await Preview(db,parent.Data.PurchaseOrderId,parentCommand)).Token;
        Assert.IsTrue((await Associate(db,parent.Data.PurchaseOrderId,parentCommand)).Success);
        var child=Guid.NewGuid();
        await db.ExecuteAsync("""
            INSERT INTO public.net_demand(uid,version,gross_demand_id,source_demand_version_snapshot,source_demand_basis_fingerprint_snapshot,
                decomposition_revision,line_number,lifecycle_status,root_net_demand_id,parent_net_demand_id,delivery_target_net_demand_id,delivery_mode,
                product_id,material_type_snapshot,supply_type_snapshot,sku_snapshot,description_snapshot,unit_snapshot,gross_qty,net_qty)
            VALUES(@child,1,@gross,1,'BASIS',1,2,0,@root,@root,@root,@mode,@ComponentId,'客供件','采购','TEST-B','客供件','PCS',10,10);
            """,new {child,gross=demand.Gross,root=demand.Net,db.ComponentId,mode=(int)DemandDeliveryModeEnum.ParentExecutionSupplier});
        async Task<(Guid Uid,AssociateProcurementDemandsCommand Command)> ChildCommand(Guid deliverySupplier)
        {
            var create=db.CreateCommand(); create.SupplierId=db.ReceiptSupplierId;
            create.Lines=[new() {ProductId=db.ComponentId,Quantity=10,UnitPrice=2,TaxPrice=2.5m,DeliverySupplierId=deliverySupplier}];
            var order=await db.CreateAsync(create); Assert.IsTrue(order.Success,order.Message);
            return (order.Data!.PurchaseOrderId,await Command(db,order.Data.PurchaseOrderId,order.Data.Lines.Single().PurchaseOrderLineId,(child,demand.Gross),4));
        }
        var invalid=await ChildCommand(db.ConsumptionSupplierId);
        var blocked=await db.InScopeAsync(p => p.GetRequiredService<IProcurementDemandAssociationService>().PreviewAsync(invalid.Uid,
            new() {ExpectedVersion=invalid.Command.ExpectedVersion,Items=invalid.Command.Items}));
        Assert.IsFalse(blocked.Success); StringAssert.Contains(blocked.Message,"送达供应商");
        var valid=await ChildCommand(db.SupplierId); valid.Command.PreviewToken=(await Preview(db,valid.Uid,valid.Command)).Token;
        Assert.IsTrue((await Associate(db,valid.Uid,valid.Command)).Success);
        Assert.AreEqual(0,await db.ScalarAsync<int>("SELECT COUNT(*) FROM public.inventory_ledger_entry"));
        Assert.AreEqual(0m,await db.ScalarAsync<decimal>("SELECT SUM(released_qty) FROM public.demand_execution_link"));
    }

    /// <summary>来源版本仅推进而实物依据不变时可沿用原拆解；预览后的当前来源版本变更仍阻断提交。</summary>
    [TestMethod]
    public async Task SourceMetadataVersionUsesBasisCompatibilityAndCurrentConcurrencyVersion()
    {
        await using var db=new IndependentProcurementPostgreSqlFixture(); await db.InitializeAsync(TestContext);
        var created=await db.CreateAsync(); var uid=created.Data!.PurchaseOrderId; var line=created.Data.Lines.Single().PurchaseOrderLineId;
        var demand=await SeedDemand(db);
        await db.ExecuteAsync("UPDATE public.gross_demand SET version=2,source_version=8 WHERE uid=@uid",new {uid=demand.Gross});
        var candidates=await db.InScopeAsync(p => p.GetRequiredService<IProcurementDemandAssociationService>().SearchCandidatesAsync(uid,
            new() {PurchaseOrderLineId=line}));
        Assert.AreEqual(1,candidates.Data!.TotalCount);
        Assert.AreEqual(2,candidates.Data.Items.Single().SourceDemandVersion);
        var command=await Command(db,uid,line,demand,6); command.PreviewToken=(await Preview(db,uid,command)).Token;
        Assert.IsTrue((await Associate(db,uid,command)).Success);
        var stale=await Command(db,uid,line,demand,2); stale.PreviewToken=(await Preview(db,uid,stale)).Token;
        await db.ExecuteAsync("UPDATE public.gross_demand SET version=3,source_version=9 WHERE uid=@uid",new {uid=demand.Gross});
        var rejected=await Associate(db,uid,stale);
        Assert.IsFalse(rejected.Success); Assert.AreEqual(ServiceErrorCodes.VersionConflict,rejected.Error!.Code);
        var changed=await Command(db,uid,line,demand,2);
        await db.ExecuteAsync("UPDATE public.gross_demand SET demand_basis_fingerprint='CHANGED' WHERE uid=@uid",new {uid=demand.Gross});
        Assert.IsFalse((await db.InScopeAsync(p => p.GetRequiredService<IProcurementDemandAssociationService>().PreviewAsync(uid,
            new() {ExpectedVersion=changed.ExpectedVersion,Items=changed.Items}))).Success);
        Assert.AreEqual(6m,await db.ScalarAsync<decimal>("SELECT SUM(allocated_qty-released_qty) FROM public.demand_execution_link"));
    }

    /// <summary>生成同客户明确单位的活动拆解事实，来源单据可复用于同单多来源行。</summary>
    private static async Task<(Guid Net,Guid Gross)> SeedDemand(IndependentProcurementPostgreSqlFixture db, Guid? sourceUid=null,bool nonStandard=false)
    {
        var gross=Guid.NewGuid(); var net=Guid.NewGuid(); var source=sourceUid??Guid.NewGuid();
        await db.ExecuteAsync("""
            INSERT INTO public.gross_demand(uid,version,lifecycle_status,source_type,source_uid,source_no,source_line_id,customer_id,
                product_id,is_non_standard,sku_snapshot,description_snapshot,unit_snapshot,demand_qty,demand_basis_fingerprint)
            VALUES(@gross,1,0,'MD',@source,'SOURCE',@gross,@CustomerId,@ProductId,@nonStandard,'TEST','明确物料','PCS',10,'BASIS');
            INSERT INTO public.net_demand(uid,version,gross_demand_id,source_demand_version_snapshot,source_demand_basis_fingerprint_snapshot,
                decomposition_revision,line_number,lifecycle_status,root_net_demand_id,delivery_mode,product_id,material_type_snapshot,supply_type_snapshot,
                sku_snapshot,description_snapshot,unit_snapshot,gross_qty,net_qty)
            VALUES(@net,1,@gross,1,'BASIS',1,1,0,@net,0,@ProductId,@MaterialType,'采购','TEST','明确物料','PCS',10,10);
            """,new {gross,net,source,db.CustomerId,ProductId=nonStandard?(Guid?)null:db.ProductId,nonStandard,MaterialType=nonStandard?null:"成品"});
        return (net,gross);
    }

    /// <summary>按实时上下文构建人工输入，测试不重算服务端预览指纹。</summary>
    private static async Task<AssociateProcurementDemandsCommand> Command(IndependentProcurementPostgreSqlFixture db,Guid uid,Guid line,(Guid Net,Guid Gross) demand,decimal qty)
    {
        var context=await db.InScopeAsync(p => p.GetRequiredService<IProcurementDemandAssociationService>().GetContextAsync(uid));
        // 作废后的失败用例直接使用数据库版本，关联上下文不应将作废采购变成可操作事实。
        var version=context.Success?context.Data!.Version:await db.ScalarAsync<int>("SELECT version FROM public.purchase_order_head WHERE uid=@uid",new {uid});
        return new() {CommandId=Guid.NewGuid(),ExpectedVersion=version,Reason="隔离测试人工关联",
            Items=[new() {CommandItemId=Guid.NewGuid(),PurchaseOrderLineId=line,
                ExpectedLineVersion=await db.ScalarAsync<int>("SELECT version FROM public.purchase_order_line WHERE uid=@line",new {line}),
                NetDemandId=demand.Net,ExpectedDemandVersion=await db.ScalarAsync<int>("SELECT version FROM public.net_demand WHERE uid=@uid",new {uid=demand.Net}),
                ExpectedSourceDemandVersion=await db.ScalarAsync<int>("SELECT version FROM public.gross_demand WHERE uid=@uid",new {uid=demand.Gross}),Quantity=qty}]};
    }

    private static Task<SwiftExport.Contracts.Wrappers.ServiceResult<ProcurementOrderCommandResult>> Associate(IndependentProcurementPostgreSqlFixture db,Guid uid,AssociateProcurementDemandsCommand command)
        => db.InScopeAsync(p => p.GetRequiredService<IProcurementDemandAssociationService>().AssociateAsync(uid,command));

    /// <summary>使用生产只读预览并保留失败消息，不能从请求自行造令牌。</summary>
    private static async Task<ProcurementDemandAssociationPreviewDto> Preview(IndependentProcurementPostgreSqlFixture db,Guid uid,AssociateProcurementDemandsCommand command)
    {
        var preview=await db.InScopeAsync(p => p.GetRequiredService<IProcurementDemandAssociationService>().PreviewAsync(uid,
            new() {ExpectedVersion=command.ExpectedVersion,Items=command.Items}));
        Assert.IsTrue(preview.Success,preview.Message); return preview.Data!;
    }
}
