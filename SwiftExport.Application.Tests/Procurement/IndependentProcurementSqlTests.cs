using SwiftExport.Contracts.DTOs.Inventory.Inbounds.Posting;
using SwiftExport.Contracts.DTOs.Inventory.Inbounds.Draft;
using Npgsql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Dapper;
using SwiftExport.Application.Interfaces.MasterData;
using SwiftExport.Application.Interfaces.Procurement;
using SwiftExport.Contracts.DTOs.MasterData.ExecutionEligibility;
using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderDetailDto;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Core.Interfaces.Procurement;
using SwiftExport.Infrastructure.Data;

namespace SwiftExport.Application.Tests.Procurement;

/// <summary>使用获批单PG隔离库验证原采购事务、快照、幂等及主数据资格；必须显式启用环境开关。</summary>
[TestClass]
[DoNotParallelize]
public sealed class IndependentProcurementSqlTests
{
    /// <summary>采购不固定收货仓；分仓过账共享采购容量，反过账仅扣回原实际仓。</summary>
    [TestMethod]
    public async Task PurchaseReceiptsAcrossWarehousesShareCapacityAndReverseOriginalWarehouse()
    {
        var created = await Db.CreateAsync();
        Assert.IsTrue(created.Success, created.Message);
        var uid = created.Data!.PurchaseOrderId;
        var context = await Db.ContextAsync(uid);
        Assert.IsTrue(context.Success, context.Message);
        Assert.IsTrue((await Db.ConfirmAsync(uid, ConfirmCommand(context.Data))).Success);
        var lineId = created.Data.Lines.Single().PurchaseOrderLineId;
        async Task<SwiftExport.Contracts.DTOs.Inventory.Inbounds.Draft.InboundDraftCommandResult> Draft(Guid site, decimal quantity)
        {
            var result = await Db.InScopeAsync(p => p.GetRequiredService<SwiftExport.Application.Interfaces.Inventory.Inbounds.IInboundDraftService>()
                .CreateAsync(new() { CommandId = Guid.NewGuid(), Draft = new()
                {
                    DocumentType = SwiftExport.Contracts.SysEnums.Inventory.InboundDocumentTypeEnum.PurchaseReceipt,
                    WarehouseSiteId = site, ReceiptDate = DateTime.Today,
                    Lines = [new() { SourceLineId = lineId, Quantity = quantity }]
                } }));
            Assert.IsTrue(result.Success, result.Message);
            return result.Data!;
        }
        async Task<SwiftExport.Contracts.Wrappers.ServiceResult<SwiftExport.Contracts.DTOs.Inventory.Inbounds.Posting.InboundPostResult>> Post(
            SwiftExport.Contracts.DTOs.Inventory.Inbounds.Draft.InboundDraftCommandResult draft) =>
            await Db.InScopeAsync(p => p.GetRequiredService<SwiftExport.Application.Interfaces.Inventory.Inbounds.IInboundPostingService>()
                .PostAsync(draft.Uid, new() { CommandId = Guid.NewGuid(), ExpectedVersion = draft.Version }));
        var first = await Draft(Db.ReceiptSiteId, 4m);
        var second = await Draft(Db.ConsumptionSiteId, 6m);
        var firstPost = await Post(first);
        var secondPost = await Post(second);
        Assert.IsTrue(firstPost.Success, firstPost.Message);
        Assert.IsTrue(secondPost.Success, secondPost.Message);
        async Task<decimal> Balance(Guid site) => await Db.ScalarAsync<decimal>(
            "SELECT on_hand_qty FROM public.inventory_balance WHERE warehouse_site_id=@site AND product_id=@product", new { site, product = Db.ProductId });
        Assert.AreEqual(4m, await Balance(Db.ReceiptSiteId));
        Assert.AreEqual(6m, await Balance(Db.ConsumptionSiteId));
        var excess = await Draft(Db.ReceiptSiteId, 1m);
        Assert.IsFalse((await Post(excess)).Success);
        var reversed = await Db.InScopeAsync(p => p.GetRequiredService<SwiftExport.Application.Interfaces.Inventory.Inbounds.IInboundReversalService>()
            .ReverseAsync(second.Uid, new() { CommandId = Guid.NewGuid(), ExpectedVersion = secondPost.Data!.Version, Reason = "隔离测试反过账" }));
        Assert.IsTrue(reversed.Success, reversed.Message);
        Assert.AreEqual(4m, await Balance(Db.ReceiptSiteId));
        Assert.AreEqual(0m, await Balance(Db.ConsumptionSiteId));
        Assert.IsTrue((await Post(excess)).Success);
    }

    /// <summary>真实 SQL 验证 Manager 头行口径一致、采购行直连、多 Link 不倍增、无 Link 可读及草稿忽略。</summary>
    [TestMethod]
    public async Task ManagerReceiptFactsUsePurchaseLineIdentityWithoutDemandLinkDependency()
    {
        var created = await Db.CreateAsync();
        Assert.IsTrue(created.Success, created.Message);
        var orderId = created.Data.PurchaseOrderId;
        var lineId = created.Data.Lines.Single().PurchaseOrderLineId;

        // 查询按收货事实展示进度；真正形成正式收货之前仍必须通过采购确认。
        var pending = await Db.InScopeAsync(async provider =>
            (await provider.GetRequiredService<IPurchaseOrderManagerQueryRepository>().SearchAsync(new())).Items.Single());
        Assert.AreEqual(0m, pending.ReceiptProgress);
        var confirmation = await Db.ContextAsync(orderId);
        Assert.IsTrue(confirmation.Success, confirmation.Message);
        Assert.IsTrue((await Db.ConfirmAsync(orderId, ConfirmCommand(confirmation.Data!))).Success);

        // 合法正式收货必须保存单位快照；缺失单位时查询应提示核实，不能冒充有效进度。
        var draftId = Guid.NewGuid();
        var postedId = Guid.NewGuid();
        await Db.ExecuteAsync("""
            INSERT INTO public.demand_execution_link
                (uid,net_demand_id,execution_request_id,command_item_id,target_type,target_head_id,target_no,target_line_id,link_status,allocated_qty,released_qty)
            VALUES
                (gen_random_uuid(),gen_random_uuid(),gen_random_uuid(),gen_random_uuid(),'PO',@OrderId,'TEST-PO',@LineId,0,5,0),
                (gen_random_uuid(),gen_random_uuid(),gen_random_uuid(),gen_random_uuid(),'PO',@OrderId,'TEST-PO',@LineId,0,5,0);

            INSERT INTO public.inbound_document(uid,document_no,document_type,creation_mode,lifecycle_status,posting_status,warehouse_site_id,receipt_date)
            VALUES(@Draft,'IR-DRAFT',10,0,0,0,@WarehouseId,LOCALTIMESTAMP),
                  (@PostedA,'IR-POSTED-A',10,0,0,1,@WarehouseId,LOCALTIMESTAMP);
            INSERT INTO public.inbound_document_line(uid,inbound_document_id,line_number,product_id,sku_snapshot,unit_snapshot,quantity,source_line_id)
            VALUES(gen_random_uuid(),@Draft,1,@ProductId,'TEST-FG','PCS',99,@LineId),
                  (gen_random_uuid(),@PostedA,1,@ProductId,'TEST-FG','PCS',6,@LineId);
            """, new { OrderId = orderId, LineId = lineId, WarehouseId = Db.ReceiptSiteId, ProductId = Db.ProductId, Draft = draftId, PostedA = postedId });

        var withMultipleLinks = await Db.InScopeAsync(async provider =>
        {
            var repository = provider.GetRequiredService<IPurchaseOrderManagerQueryRepository>();
            var line = (await repository.GetLinesAsync(orderId)).Single();
            var head = (await repository.SearchAsync(new())).Items.Single(x => x.PurchaseOrderId == orderId);
            return (line, head);
        });
        Assert.AreEqual(6m, withMultipleLinks.line.ReceivedQty);
        Assert.AreEqual(0.6m, withMultipleLinks.head.ReceiptProgress);

        await Db.ExecuteAsync("UPDATE public.inbound_document_line SET unit_snapshot=NULL WHERE source_line_id=@LineId", new { LineId = lineId });
        var invalidUnit = await Db.InScopeAsync(async provider =>
            (await provider.GetRequiredService<IPurchaseOrderManagerQueryRepository>().SearchAsync(new())).Items.Single());
        Assert.IsNull(invalidUnit.ReceiptProgress);
        await Db.ExecuteAsync("UPDATE public.inbound_document_line SET unit_snapshot='PCS' WHERE source_line_id=@LineId", new { LineId = lineId });

        await Db.ExecuteAsync("DELETE FROM public.demand_execution_link WHERE target_head_id=@OrderId;", new { OrderId = orderId });
        var noLinkReceipt = await Db.InScopeAsync(async provider =>
            (await provider.GetRequiredService<IPurchaseOrderManagerQueryRepository>().GetLinesAsync(orderId)).Single());
        Assert.AreEqual(6m, noLinkReceipt.ReceivedQty);

        var unknownId = Guid.NewGuid();
        await Db.ExecuteAsync("""
            INSERT INTO public.inbound_document(uid,document_no,document_type,creation_mode,lifecycle_status,posting_status,warehouse_site_id,receipt_date)
            VALUES(@Unknown,'IR-UNKNOWN',10,0,0,1,@WarehouseId,LOCALTIMESTAMP);
            INSERT INTO public.inbound_document_line(uid,inbound_document_id,line_number,product_id,sku_snapshot,quantity,source_line_id)
            VALUES(gen_random_uuid(),@Unknown,1,@ProductId,'TEST-FG',1,gen_random_uuid());
            """, new { WarehouseId = Db.ReceiptSiteId, ProductId = Db.ProductId, Unknown = unknownId });

        var blocked = await Db.InScopeAsync(provider =>
            provider.GetRequiredService<IPurchaseOrderManagerQueryService>().GetLinesAsync(orderId));
        Assert.IsFalse(blocked.Success);
        Assert.AreEqual(SwiftExport.Contracts.DTOs.Errors.ServiceErrorCodes.BusinessBlocked, blocked.Error.Code);
        StringAssert.Contains(blocked.Message, "无法定位采购行");
    }

    /// <summary>保存原测试身份供导出证据关联。</summary>
    public TestContext TestContext { get; set; } = default!;

    private IndependentProcurementPostgreSqlFixture? fixture;
    private IndependentProcurementPostgreSqlFixture Db => fixture!;

    /// <summary>未显式开启时标记未执行，绝不悄悄改为假仓储测试。</summary>
    [TestInitialize]
    public async Task InitializeAsync()
    {
        if (Environment.GetEnvironmentVariable("SWIFTEXPORT_RUN_PG_LEGACY_TESTS") != "1")
            Assert.Inconclusive("设置 SWIFTEXPORT_RUN_PG_LEGACY_TESTS=1 后运行获批单库PG集成测试。");
        fixture = new IndependentProcurementPostgreSqlFixture();
        await fixture.InitializeAsync(TestContext);
    }

    /// <summary>释放本例全部连接、导出完整事实后，仅重置获批新测试库public结构。</summary>
    [TestCleanup]
    public async Task CleanupAsync()
    {
        if (fixture != null) await fixture.DisposeAsync();
    }

    /// <summary>无需采购收货仓即可创建并确认订单，冻结客户、单位与实际供应商，不生成库存。</summary>
    [TestMethod]
    public async Task CreateAndConfirmWithoutBomDoesNotCreateDemandOrInventory()
    {
        var created = await Db.CreateAsync();
        Assert.IsTrue(created.Success, created.Message);
        var uid = created.Data.PurchaseOrderId;
        Assert.AreEqual(Db.SupplierId, await Db.ScalarAsync<Guid>("SELECT supplier_id FROM public.purchase_order_head WHERE uid=@uid", new { uid }));
        Assert.AreEqual(Db.CustomerId, await Db.ScalarAsync<Guid>("SELECT customer_id FROM public.purchase_order_head WHERE uid=@uid", new { uid }));
        Assert.IsNull(await Db.ScalarAsync<Guid?>("SELECT delivery_target_supplier_id FROM public.purchase_order_line WHERE purchase_order_id=@uid", new { uid }));
        Assert.AreEqual("PCS", await Db.ScalarAsync<string>("SELECT unit_snapshot FROM public.purchase_order_line WHERE purchase_order_id=@uid", new { uid }));
        Assert.AreEqual(10m, await Db.ScalarAsync<decimal>("SELECT unlinked_qty FROM public.purchase_order_line WHERE purchase_order_id=@uid", new { uid }));
        Assert.AreEqual(0, await Db.ScalarAsync<int>("SELECT COUNT(*) FROM public.purchase_order_head WHERE uid=@uid AND (demand_source_uid IS NOT NULL OR demand_source_type IS NOT NULL OR demand_source_no IS NOT NULL)", new { uid }));
        var context = await Db.ContextAsync(uid);
        Assert.IsTrue(context.Success, context.Message);
        Assert.AreEqual(1, context.Data.Lines.Count);
        var confirmed = await Db.ConfirmAsync(uid, ConfirmCommand(context.Data));
        Assert.IsTrue(confirmed.Success, confirmed.Message);
        Assert.AreEqual(0, await Db.ScalarAsync<int>("SELECT COUNT(*) FROM public.purchase_confirmation_line WHERE purchase_order_id=@uid AND has_bom=FALSE", new { uid }));
        Assert.AreEqual(0, await Db.ScalarAsync<int>("SELECT (SELECT COUNT(*) FROM public.gross_demand)+(SELECT COUNT(*) FROM public.net_demand)+(SELECT COUNT(*) FROM public.demand_execution_link)+(SELECT COUNT(*) FROM public.inventory_ledger_entry)"));
    }

    /// <summary>客供件必须显式提交送达供应商，服务端保存所选身份快照而不从收货仓推导。</summary>
    [TestMethod]
    public async Task CustomerSuppliedLineRequiresExplicitDeliverySupplier()
    {
        await Db.ExecuteAsync("UPDATE public.product SET material_type='客供件' WHERE uid=@uid", new { uid = Db.ProductId });
        var missing = await Db.CreateAsync();
        Assert.IsFalse(missing.Success);
        StringAssert.Contains(missing.Message, "送达供应商");

        var command = Db.CreateCommand();
        command.Lines[0].DeliverySupplierId = Db.SupplierId;
        var created = await Db.CreateAsync(command);
        Assert.IsTrue(created.Success, created.Message);
        var uid = created.Data.PurchaseOrderId;
        Assert.AreEqual(Db.SupplierId, await Db.ScalarAsync<Guid>("SELECT delivery_target_supplier_id FROM public.purchase_order_line WHERE purchase_order_id=@uid", new { uid }));
        Assert.AreEqual("TEST-ACTUAL", await Db.ScalarAsync<string>("SELECT delivery_supplier_code_snapshot FROM public.purchase_order_line WHERE purchase_order_id=@uid", new { uid }));
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
        Assert.AreEqual(Db.ProductId, await Db.ScalarAsync<Guid>("SELECT product_id FROM public.purchase_order_line WHERE purchase_order_id=@uid", new { uid }));
        Assert.AreEqual(10m, await Db.ScalarAsync<decimal>("SELECT quantity FROM public.purchase_order_line WHERE purchase_order_id=@uid", new { uid }));
        await Db.ExecuteAsync("UPDATE public.supplier SET supplier_name='当前名称已改变' WHERE uid=@uid", new { uid = Db.SupplierId });
        var replay = await Db.CreateAsync(command);
        Assert.IsTrue(replay.Success, replay.Message);
        Assert.AreEqual(1, replay.Data.Version);
        Assert.AreEqual(uid, replay.Data.PurchaseOrderId);
        Assert.AreEqual("实际供应商", await Db.ScalarAsync<string>("SELECT supplier_name FROM public.purchase_order_head WHERE uid=@uid", new { uid }));
        command.Lines[0].Quantity = 20;
        Assert.IsFalse((await Db.CreateAsync(command)).Success);
        command.Lines[0].Quantity = 10;
        Db.ActorId = Db.OtherActorId;
        Assert.IsFalse((await Db.CreateAsync(command)).Success);
        Assert.AreEqual(2, await Db.ScalarAsync<int>("SELECT COUNT(*) FROM public.command_receipt"));
    }

    /// <summary>含客供件的采购不选消耗仓即可确认，命令重试保持同一批次。</summary>
    [TestMethod]
    public async Task BomConfirmationDoesNotBindWarehouseOrCreateSnapshot()
    {
        await Db.AddBomAsync();
        var created = await Db.CreateAsync();
        var uid = created.Data.PurchaseOrderId;
        var context = await Db.ContextAsync(uid);
        Assert.IsTrue(context.Success, context.Message);
        var command = ConfirmCommand(context.Data);
        var confirmed = await Db.ConfirmAsync(uid, command);
        Assert.IsTrue(confirmed.Success, confirmed.Message);
        Assert.AreEqual(0, await Db.ScalarAsync<int>("SELECT COUNT(*) FROM public.purchase_confirmation_node"));
        Assert.AreEqual(0, await Db.ScalarAsync<int>("SELECT COUNT(*) FROM public.purchase_confirmation_line"));
        Assert.IsNotNull(confirmed.Data.ConfirmationId);
        var replay = await Db.ConfirmAsync(uid, command);
        Assert.IsTrue(replay.Success, replay.Message);
        Assert.AreEqual(confirmed.Data.ConfirmationId, replay.Data.ConfirmationId);

    }

    /// <summary>BOM 改变不影响采购事实指纹。</summary>
    [TestMethod]
    public async Task ChangedBomDoesNotInvalidateConfirmation()
    {
        await Db.AddBomAsync();
        var created = await Db.CreateAsync();
        Assert.IsTrue(created.Success, created.Message);
        var context = await Db.ContextAsync(created.Data.PurchaseOrderId);
        Assert.IsTrue(context.Success, context.Message);
        var command = ConfirmCommand(context.Data);
        await Db.ExecuteAsync("UPDATE public.bom SET numerator=3,version=version+1 WHERE uid=@uid", new { uid = Db.BomLineId });
        var failed = await Db.ConfirmAsync(created.Data.PurchaseOrderId, command);
        Assert.IsTrue(failed.Success, failed.Message);
        Assert.AreEqual(0, await Db.ScalarAsync<int>("SELECT COUNT(*) FROM public.purchase_confirmation_line"));
        Assert.AreEqual(1, await Db.ScalarAsync<int>("SELECT confirmation_status FROM public.purchase_order_head"));
        Assert.AreEqual(2, await Db.ScalarAsync<int>("SELECT COUNT(*) FROM public.command_receipt"));
    }

    /// <summary>缺失主数据和失效单位仍阻断，BOM 子项损坏不影响采购确认。</summary>
    [TestMethod]
    public async Task MissingMasterInvalidUnitAndBrokenBomAreRejected()
    {
        var missing = Db.CreateCommand();
        missing.CustomerId = Guid.NewGuid();
        Assert.IsFalse((await Db.CreateAsync(missing)).Success);
        await Db.ExecuteAsync("UPDATE public.sys_dict_data SET is_enabled=FALSE");
        Assert.IsFalse((await Db.CreateAsync()).Success);
        Assert.AreEqual(0, await Db.ScalarAsync<int>("SELECT COUNT(*) FROM public.purchase_order_head"));
        await Db.ExecuteAsync("UPDATE public.sys_dict_data SET is_enabled=TRUE");
        await Db.AddBomAsync();
        var created = await Db.CreateAsync();
        Assert.IsTrue(created.Success, created.Message);
        await Db.ExecuteAsync("UPDATE public.bom SET component_product_id=gen_random_uuid() WHERE uid=@uid", new { uid = Db.BomLineId });
        var context = await Db.ContextAsync(created.Data.PurchaseOrderId);
        Assert.IsTrue(context.Success, context.Message);
        Assert.AreEqual(0, await Db.ScalarAsync<int>("SELECT COUNT(*) FROM public.purchase_confirmation_line"));
    }

    /// <summary>两个并发确认只有一个成功；版本锁和事务确保只形成一个确认批次。</summary>
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
        Assert.AreEqual(1, await Db.ScalarAsync<int>("SELECT COUNT(DISTINCT active_confirmation_id) FROM public.purchase_order_head"));
        Assert.AreEqual(2, await Db.ScalarAsync<int>("SELECT COUNT(*) FROM public.command_receipt"));
    }

    /// <summary>真实日志 SQL 故障回滚采购头、行、编号序列和幂等回执。</summary>
    [TestMethod]
    public async Task LogFailureRollsBackCreationAndCommandReceipt()
    {
        await Db.RejectLogsAsync();
        var failed = await Db.CreateAsync();
        Assert.IsFalse(failed.Success);
        Assert.AreEqual(0, await Db.ScalarAsync<int>("SELECT COUNT(*) FROM public.purchase_order_head"));
        Assert.AreEqual(0, await Db.ScalarAsync<int>("SELECT COUNT(*) FROM public.purchase_order_line"));
        Assert.AreEqual(0, await Db.ScalarAsync<int>("SELECT COUNT(*) FROM public.command_receipt"));
        Assert.AreEqual(0, await Db.ScalarAsync<int>("SELECT COUNT(*) FROM public.document_sequence"));
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
        await Db.RejectLogsAsync();
        Assert.IsFalse((await Db.ConfirmAsync(uid, command)).Success);
        Assert.AreEqual(0, await Db.ScalarAsync<int>("SELECT COUNT(*) FROM public.purchase_confirmation_line"));
        Assert.AreEqual(0, await Db.ScalarAsync<int>("SELECT COUNT(*) FROM public.purchase_confirmation_node"));
        Assert.AreEqual(0, await Db.ScalarAsync<int>("SELECT confirmation_status FROM public.purchase_order_head"));
        Assert.AreEqual(1, await Db.ScalarAsync<int>("SELECT COUNT(*) FROM public.command_receipt"));
        await Db.ExecuteAsync("DROP TRIGGER tr_test_reject_log ON public.business_operation_log; DROP FUNCTION public.p09_test_reject_log();");
        var retried = await Db.ConfirmAsync(uid, command);
        Assert.IsTrue(retried.Success, retried.Message);
    }

    /// <summary>合法 BOM 没有客供件仍保存结构依据，与根本没有 BOM 的订单明确区分。</summary>
    [TestMethod]
    public async Task BomWithoutCustomerSuppliedNodesKeepsBomSnapshotWithoutConsumptionBinding()
    {
        await Db.AddBomAsync();
        await Db.ExecuteAsync("UPDATE public.product SET material_type='零件' WHERE uid=@uid", new { uid = Db.ComponentId });
        var created = await Db.CreateAsync();
        Assert.IsTrue(created.Success, created.Message);
        var uid = created.Data.PurchaseOrderId;
        var context = await Db.ContextAsync(uid);
        Assert.IsTrue(context.Success, context.Message);
        Assert.AreEqual(1, context.Data.Lines.Count);
        var confirmed = await Db.ConfirmAsync(uid, ConfirmCommand(context.Data));
        Assert.IsTrue(confirmed.Success, confirmed.Message);
        Assert.AreEqual(0, await Db.ScalarAsync<int>("SELECT COUNT(*) FROM public.purchase_confirmation_line WHERE has_bom=TRUE"));
        Assert.AreEqual(0, await Db.ScalarAsync<int>("SELECT COUNT(*) FROM public.purchase_confirmation_node WHERE consumption_warehouse_site_id IS NULL"));
    }

    /// <summary>真实账号停用后即使已有成功命令回执也拒绝重放，不通过旧回执绕过权限。</summary>
    [TestMethod]
    public async Task RevokedAccountCannotReplayPreviouslySuccessfulCommand()
    {
        var command = Db.CreateCommand();
        var created = await Db.CreateAsync(command);
        Assert.IsTrue(created.Success, created.Message);
        await Db.ExecuteAsync("UPDATE public.sys_user SET is_active=FALSE WHERE uid=@uid", new { uid = Db.ActorId });
        Assert.IsFalse((await Db.CreateAsync(command)).Success);
        Assert.AreEqual(1, await Db.ScalarAsync<int>("SELECT COUNT(*) FROM public.command_receipt"));
    }

    /// <summary>非管理员只授予创建角色时不能读取、编辑或确认；撤销真实角色关联后连成功回执也不能重放。</summary>
    [TestMethod]
    public async Task OperationRoleGrantsOnlyItsCommandAndRevocationBlocksReplay()
    {
        var permissionId = Guid.NewGuid();
        await Db.ExecuteAsync("""
            UPDATE public.sys_role SET role_code='CREATE_ONLY',role_name='仅创建',is_system=FALSE WHERE uid=@RoleId;
            INSERT INTO public.sys_permission(uid,permission_key,display_name,group_key,sort_order,is_active)
            VALUES(@PermissionId,'Procurement.Order.Create','采购创建','Procurement',1,TRUE);
            INSERT INTO public.sys_role_permission(uid,role_id,permission_id) VALUES(gen_random_uuid(),@RoleId,@PermissionId);
            """, new { Db.RoleId, PermissionId = permissionId });
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
        await Db.ExecuteAsync("UPDATE public.sys_user_role SET is_deleted=TRUE WHERE user_id=@uid", new { uid = Db.ActorId });
        var replay = await Db.CreateAsync(command);
        Assert.IsFalse(replay.Success);
        StringAssert.Contains(replay.Message, "权限");
        Assert.AreEqual(1, await Db.ScalarAsync<int>("SELECT COUNT(*) FROM public.command_receipt"));
        Assert.AreEqual(1, await Db.ScalarAsync<int>("SELECT version FROM public.purchase_order_head WHERE uid=@uid", new { uid }));
    }

    /// <summary>普通用户显式获得采购创建和读取权限后，可完成创建并读取上下文。</summary>
    [TestMethod]
    public async Task ExplicitProcurementRoleGrantsCreateAndRead()
    {
        var roleId = Guid.NewGuid();
        await Db.ExecuteAsync("""
            UPDATE public.sys_user_role SET is_deleted=TRUE WHERE user_id=@ActorId;
            INSERT INTO public.sys_role(uid,role_code,role_name,is_active,is_system)
            VALUES(@RoleId,'PROCUREMENT_CREATE_READ','采购创建读取',TRUE,FALSE);
            INSERT INTO public.sys_user_role(uid,user_id,role_id) VALUES(gen_random_uuid(),@ActorId,@RoleId);
            INSERT INTO public.sys_permission(uid,permission_key,display_name,group_key,sort_order,is_active)
            VALUES(gen_random_uuid(),'Procurement.Order.Create','采购创建','Procurement',1,TRUE),
                  (gen_random_uuid(),'Procurement.Order.Read','采购读取','Procurement',2,TRUE);
            INSERT INTO public.sys_role_permission(uid,role_id,permission_id)
            SELECT gen_random_uuid(),@RoleId,uid FROM public.sys_permission
            WHERE permission_key IN ('Procurement.Order.Create','Procurement.Order.Read');
            """, new { Db.ActorId, RoleId = roleId });
        var created = await Db.CreateAsync();
        Assert.IsTrue(created.Success, created.Message);
        var context = await Db.ContextAsync(created.Data.PurchaseOrderId);
        Assert.IsTrue(context.Success, context.Message);
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
        await Db.ExecuteAsync("UPDATE public.sys_role SET is_deleted=TRUE WHERE uid=@uid", new { uid = Db.RoleId });
        Assert.IsFalse((await Db.InScopeAsync(p => p.GetRequiredService<IExecutionEligibilityService>().SetStatusAsync(command))).Success);
        await Db.ExecuteAsync("UPDATE public.sys_role SET is_deleted=FALSE WHERE uid=@uid", new { uid = Db.RoleId });
        await Db.RejectLogsAsync();
        await Assert.ThrowsAsync<PostgresException>(() => Db.InScopeAsync(p => p.GetRequiredService<IExecutionEligibilityService>().SetStatusAsync(command)));
        Assert.IsFalse(await Db.ScalarAsync<bool>("SELECT is_active FROM public.supplier WHERE uid=@uid", new { uid = Db.SupplierId }));
        Assert.AreEqual(2, await Db.ScalarAsync<int>("SELECT version FROM public.supplier WHERE uid=@uid", new { uid = Db.SupplierId }));
        Assert.AreEqual(1, await Db.ScalarAsync<int>("SELECT COUNT(*) FROM public.business_operation_log WHERE module='MasterData'"));
    }

    /// <summary>已有采购引用后禁止直接修改产品基础单位，余额是否为零不参与放行。</summary>
    [TestMethod]
    public async Task ReferencedProductUnitCannotBeChanged()
    {
        // 未引用时允许纠正单位；冻结防线不能把所有产品单位无条件设为不可变。
        await Db.ExecuteAsync("UPDATE public.product SET unit='KG' WHERE uid=@uid", new { uid = Db.ProductId });
        Assert.AreEqual("KG", await Db.ScalarAsync<string>("SELECT unit FROM public.product WHERE uid=@uid", new { uid = Db.ProductId }));
        await Db.ExecuteAsync("UPDATE public.product SET unit='PCS' WHERE uid=@uid", new { uid = Db.ProductId });
        var created = await Db.CreateAsync();
        Assert.IsTrue(created.Success, created.Message);
        Assert.AreEqual(1, await Db.ScalarAsync<int>("SELECT COUNT(*) FROM pg_trigger t JOIN pg_class c ON c.oid=t.tgrelid JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='public' AND c.relname='product' AND t.tgname='tr_product_unit_frozen_on_reference' AND NOT t.tgisinternal AND t.tgenabled<>'D'"));
        var error = await Assert.ThrowsAsync<PostgresException>(() => Db.ExecuteAsync("UPDATE public.product SET unit='KG' WHERE uid=@uid", new { uid = Db.ProductId }));
        Assert.AreEqual("P3102", error.SqlState);
        Assert.AreEqual("PCS", await Db.ScalarAsync<string>("SELECT unit FROM public.product WHERE uid=@uid", new { uid = Db.ProductId }));
        await Assert.ThrowsAsync<PostgresException>(() => Db.ExecuteAsync("UPDATE public.product SET unit=NULL WHERE uid=@uid", new { uid = Db.ProductId }));
        await Db.ExecuteAsync("UPDATE public.product SET unit='PCS' WHERE uid=@uid", new { uid = Db.ProductId });
        Assert.AreEqual("PCS", await Db.ScalarAsync<string>("SELECT unit_snapshot FROM public.purchase_order_line WHERE purchase_order_id=@uid", new { uid = created.Data!.PurchaseOrderId }));
        // 多行更新中任一产品已冻结时整条语句回滚，不能留下部分单位变更。
        await Assert.ThrowsAsync<PostgresException>(() => Db.ExecuteAsync("UPDATE public.product SET unit='KG' WHERE uid IN (@ProductId,@ComponentId)", new { Db.ProductId, Db.ComponentId }));
        Assert.AreEqual(2, await Db.ScalarAsync<int>("SELECT COUNT(*) FROM public.product WHERE unit='PCS'"));
    }

    /// <summary>无采购引用但已有正式库存流水的产品也冻结单位；库存归零不解除历史单位边界。</summary>
    [TestMethod]
    public async Task InventoryHistoryFreezesProductUnitEvenWhenBalanceIsZero()
    {
        await Db.ExecuteAsync("""
            INSERT INTO public.inventory_ledger_entry(uid,sequence,warehouse_site_id,product_id,transaction_type,quantity_delta,balance_after,
                source_document_type,command_id,command_item_id,occurred_date,unit_snapshot)
            VALUES(gen_random_uuid(),nextval('public.inventory_ledger_sequence'),@WarehouseId,@ProductId,0,5,5,'TEST',gen_random_uuid(),gen_random_uuid(),LOCALTIMESTAMP,'PCS'),
                  (gen_random_uuid(),nextval('public.inventory_ledger_sequence'),@WarehouseId,@ProductId,0,-5,0,'TEST',gen_random_uuid(),gen_random_uuid(),LOCALTIMESTAMP,'PCS');
            INSERT INTO public.inventory_balance(uid,warehouse_site_id,product_id,on_hand_qty,last_ledger_sequence,unit_snapshot)
            VALUES(gen_random_uuid(),@WarehouseId,@ProductId,0,2,'PCS');
            """, new { WarehouseId = Db.ReceiptSiteId, Db.ProductId });
        Assert.AreEqual(0, await Db.ScalarAsync<int>("SELECT COUNT(*) FROM public.purchase_order_line"));
        Assert.AreEqual(0m, await Db.ScalarAsync<decimal>("SELECT on_hand_qty FROM public.inventory_balance WHERE product_id=@ProductId", new { Db.ProductId }));
        var error = await Assert.ThrowsAsync<PostgresException>(() => Db.ExecuteAsync("UPDATE public.product SET unit='KG' WHERE uid=@ProductId", new { Db.ProductId }));
        Assert.AreEqual("P3102", error.SqlState);
        Assert.AreEqual("PCS", await Db.ScalarAsync<string>("SELECT unit FROM public.product WHERE uid=@ProductId", new { Db.ProductId }));
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
        await Db.ExecuteAsync("UPDATE public.product SET material_type='客供件',version=version+1 WHERE uid=@uid", new { uid = Db.ProductId });
        Assert.IsFalse((await Db.ContextAsync(uid)).Success);
        Assert.IsFalse((await Db.ConfirmAsync(uid, ConfirmCommand(before.Data))).Success);
        Assert.AreEqual(0, await Db.ScalarAsync<int>("SELECT COUNT(*) FROM public.purchase_confirmation_line"));
    }

    /// <summary>采购确认预览不阻塞 BOM 更新，更新后采购事实指纹不变。</summary>
    [TestMethod]
    public async Task BomUpdateWaitsForCoherentReadTransaction()
    {
        await Db.AddBomAsync();
        var created = await Db.CreateAsync();
        Assert.IsTrue(created.Success, created.Message);
        var uid = created.Data.PurchaseOrderId;
        var fingerprint = await ReadWhileBomWriterCompletesAsync(uid,
            "UPDATE public.bom SET numerator=5,version=version+1 WHERE uid=@BomLineId;",
            new { Db.BomLineId });
        var after = await Db.ContextAsync(uid);
        Assert.IsTrue(after.Success, after.Message);
        Assert.AreEqual(fingerprint, after.Data.Fingerprint);
    }

    /// <summary>采购确认不锁定空 BOM 范围，插入组成不改变采购确认资格。</summary>
    [TestMethod]
    public async Task EmptyBomRangeBlocksConcurrentInsertion()
    {
        var created = await Db.CreateAsync();
        Assert.IsTrue(created.Success, created.Message);
        var uid = created.Data.PurchaseOrderId;
        await ReadWhileBomWriterCompletesAsync(uid, """
            INSERT INTO public.bom(uid,bom_no,bom_version,parent_product_id,parent_sku_snapshot,line_number,component_product_id,component_sku_snapshot,numerator,denominator)
            VALUES(@BomLineId,'TEST-BOM',1,@ProductId,'TEST-FG',1,@ComponentId,'TEST-B',2,3);
            """, new { Db.BomLineId, Db.ProductId, Db.ComponentId });
        Assert.IsTrue((await Db.ContextAsync(uid)).Success);
    }

    /// <summary>在采购读取事务尚未提交时完成独立 BOM 写入，证明已解除结构范围锁依赖。</summary>
    private async Task<string> ReadWhileBomWriterCompletesAsync(Guid uid, string sql, object args)
    {
        return await Db.InScopeAsync(async provider =>
        {
            var session = provider.GetRequiredService<DbSession>();
            using var transaction = session.Connection.BeginTransaction(System.Data.IsolationLevel.Serializable);
            session.Transaction = transaction;
            await using var writer = await Db.OpenConnectionAsync();
            var readerId = await session.Connection.ExecuteScalarAsync<int>("SELECT pg_backend_pid()", transaction: transaction);
            var writerId = await writer.ExecuteScalarAsync<int>("SELECT pg_backend_pid()");
            Task<int>? pending = null;
            try
            {
                var repository = provider.GetRequiredService<IProcurementOrderDocumentRepository>();
                var head = await repository.GetHeadAsync(uid, default);
                var lines = await repository.GetLinesAsync(uid, default);
                var context = await provider.GetRequiredService<ProcurementConfirmationBuilder>().BuildAsync(ProcurementOrderCommandSupport.RequireLockedFacts(await provider.GetRequiredService<IProcurementReversalRepository>().LockAsync(uid, default)), default);
                pending = writer.ExecuteAsync(sql, args, commandTimeout: 20);
                Assert.AreEqual(1, await pending.WaitAsync(TimeSpan.FromSeconds(5)), "采购确认读取不应锁定 BOM 范围。");
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
            PurchaseOrderLineId = l.PurchaseOrderLineId, ExpectedVersion = l.Version, UnitPrice = 2m, TaxPrice = 3m,
            SupplierSku = "SUPPLIER-UPDATED", PlannedDeliveryDate = new DateTime(2026, 9, 10)
        }).ToList()
    };

    private static ConfirmProcurementOrderCommand ConfirmCommand(ProcurementConfirmationContextDto context) => new()
    {
        CommandId = Guid.NewGuid(), ExpectedVersion = context.Version, ContextFingerprint = context.Fingerprint
    };

}
