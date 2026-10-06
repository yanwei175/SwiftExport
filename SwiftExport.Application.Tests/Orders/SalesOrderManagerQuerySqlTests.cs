using Microsoft.Extensions.DependencyInjection;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using SwiftExport.Application.Tests.Database;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;
using SwiftExport.WebApi.Configuration;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Interfaces.SalesOrders.Query;
using SwiftExport.Application.Services.DemandPlanning.Sources.Policies;
using SwiftExport.Contracts.DTOs.SalesOrders.Manager;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Core.Interfaces.SalesOrders.Query;
using SwiftExport.Core.Entities.DemandPlanning;

namespace SwiftExport.Application.Tests.Orders;

/// <summary>原销售管理场景接真实PG查询及履约服务；只写随机新事实，保护既有数据，不初始化或清理库。</summary>
[TestClass, DoNotParallelize, TestCategory("PostgreSqlIntegration")]
public sealed class SalesOrderManagerQueryPostgreSqlTests
{
    /// <summary>草稿零交付、已确认部分交付及反向变化共享同一行级计算口径。</summary>
    [TestMethod]
    public async Task DraftPostedAndReversedDelivery_KeepListDetailAndFiltersConsistent()
    {
        await using var db = new SalesFixture();
        await db.InitializeAsync();
        var draft = Guid.NewGuid(); var draftLine = Guid.NewGuid();
        var confirmed = Guid.NewGuid(); var confirmedLine = Guid.NewGuid();
        var demand = Guid.NewGuid(); var packing = Guid.NewGuid(); var packingLine = Guid.NewGuid();
        var fingerprint = SourceDemandBasisFingerprintFactory.Create(new GrossDemand
        {
            CustomerId = db.CustomerId, IsNonStandard = true, SkuSnapshot = db.ConfirmedSku,
            CustomerSkuSnapshot = "CUSTOM-CONF", UnitSnapshot = "PCS", DemandQty = 10,
            RequiredDate = new DateTime(2026, 10, 2)
        });
        await db.ExecuteAsync("""
            insert into public.sales_order_header(uid,document_status,confirmation_status,order_no,invoice_no,customer_id,customer_code,lead_time_days,order_date,delivery_date)
            values(@draft,0,0,@DraftNo,'INV-DRAFT',@customerId,@CustomerCode,7,'2026-09-01','2026-10-01'),
                  (@confirmed,0,1,@ConfirmedNo,'INV-CONFIRMED',@customerId,@CustomerCode,7,'2026-09-02','2026-10-02');
            insert into public.sales_order_line(uid,line_number,sales_order_id,order_no,is_non_standard,product_id,sku,customer_sku,qty,unit_snapshot,expected_delivery_date)
            values(@draftLine,1,@draft,@DraftNo,TRUE,null,'DRAFT-SKU','CUSTOM-DRAFT',10,'PCS','2026-10-01'),
                  (@confirmedLine,1,@confirmed,@ConfirmedNo,TRUE,null,@ConfirmedSku,'CUSTOM-CONF',10,'PCS','2026-10-02');
            insert into public.gross_demand(uid,lifecycle_status,source_type,source_uid,source_no,source_line_id,source_line_number,source_version,demand_basis_fingerprint,
                                    customer_id,is_non_standard,sku_snapshot,customer_sku_snapshot,unit_snapshot,demand_qty,required_date)
            values(@demand,0,'SO',@confirmed,@ConfirmedNo,@confirmedLine,1,1,@fingerprint,@customerId,TRUE,@ConfirmedSku,'CUSTOM-CONF','PCS',10,'2026-10-02');
            insert into public.packing_list_head(uid,packing_list_no,customer_id,invoice_date,lifecycle_status,posting_status,posting_date,posted_date,create_command_id)
            values(@packing,@PackingNo,@customerId,'2026-09-02',0,1,'2026-09-02','2026-09-02',gen_random_uuid());
            insert into public.packing_list_line(uid,packing_list_head_id,line_number,gross_demand_id,is_non_standard_snapshot,source_type_snapshot,source_document_no_snapshot,
                                         source_line_number_snapshot,sku_snapshot,unit_snapshot,qty)
            values(@packingLine,@packing,1,@demand,TRUE,'SO',@ConfirmedNo,1,@ConfirmedSku,'PCS',4);
            """, new { draft, draftLine, confirmed, confirmedLine, demand, packing, packingLine, fingerprint, customerId = db.CustomerId });

        // 随机客户仅隔离原场景事实，所有快捷视图、数量和分页条件保持。
        async Task<SalesOrderManagerListItemDto> Single(SalesOrderManagerSearchRequest request)
        {
            request.CustomerId = db.CustomerId;
            var result = await db.InScopeAsync(p => p.GetRequiredService<ISalesOrderQueryService>().SearchAsync(request));
            Assert.IsTrue(result.Success, result.Message);
            Assert.AreEqual(1, result.Data!.TotalCount);
            return result.Data.Items.Single();
        }
        var draftItem = await Single(new() { QuickView = SalesOrderManagerQuickView.PendingConfirmation });
        Assert.AreEqual(0m, draftItem.TotalShippedQty);
        Assert.AreEqual(10m, draftItem.TotalUnshippedQty);
        var draftFulfillment = await db.InScopeAsync(p => p.GetRequiredService<ISalesFulfillmentQueryService>().GetAsync(draft));
        Assert.AreEqual(0m, draftFulfillment.Data!.Lines.Single().ShippedQty);
        Assert.AreEqual(10m, draftFulfillment.Data.Lines.Single().UnshippedQty);
        Assert.AreEqual("待确认", draftFulfillment.Data.Lines.Single().SourceState);
        Assert.AreEqual("未关联", draftFulfillment.Data.Lines.Single().PurchaseState);
        var item = await Single(new() { QuickView = SalesOrderManagerQuickView.PartiallyShipped });
        Assert.AreEqual(4m, item.TotalShippedQty);
        Assert.AreEqual(6m, item.TotalUnshippedQty);
        Assert.AreEqual(SalesOrderFulfillmentStatusEnum.PartiallyShipped, item.FulfillmentStatus);
        Assert.AreEqual("INV-CONFIRMED", item.InvoiceNo);
        var detail = await db.InScopeAsync(p => p.GetRequiredService<ISalesOrderQueryService>().GetAsync(confirmed));
        Assert.IsTrue(detail.Success, detail.Message);
        Assert.AreEqual(4m, detail.Data!.Lines.Single().ShippedQty);
        var fulfillment = await db.InScopeAsync(p => p.GetRequiredService<ISalesFulfillmentQueryService>().GetAsync(confirmed));
        Assert.IsTrue(fulfillment.Success, fulfillment.Message);
        Assert.AreEqual(4m, fulfillment.Data!.Lines.Single().ShippedQty);
        Assert.AreEqual(6m, fulfillment.Data.Lines.Single().UnshippedQty);
        Assert.AreEqual(demand, fulfillment.Data.Lines.Single().GrossDemandId);
        Assert.AreEqual(1, fulfillment.Data.Lines.Single().Deliveries.Count);
        var net = Guid.NewGuid(); var otherGross = Guid.NewGuid(); var otherNet = Guid.NewGuid();
        var purchase = Guid.NewGuid(); var purchaseLine = Guid.NewGuid();
        var independent = Guid.NewGuid(); var independentLine = Guid.NewGuid();
        var ownLink = Guid.NewGuid(); var sharedLink = Guid.NewGuid();
        await db.ExecuteAsync("""
            insert into public.net_demand(uid,gross_demand_id,source_demand_version_snapshot,source_version_snapshot,source_demand_basis_fingerprint_snapshot,
                decomposition_revision,line_number,lifecycle_status,root_net_demand_id,delivery_mode,supply_type_snapshot,sku_snapshot,gross_qty,net_qty)
            values(@net,@demand,(select version from public.gross_demand where uid=@demand),1,@fingerprint,
                1,1,0,@net,0,'采购',@ConfirmedSku,10,10);
            insert into public.gross_demand(uid,lifecycle_status,source_type,source_uid,source_no,source_line_id,is_non_standard,sku_snapshot,demand_qty)
            values(@otherGross,0,'Manual',gen_random_uuid(),'OTHER',gen_random_uuid(),TRUE,'OTHER-SKU',2);
            insert into public.net_demand(uid,gross_demand_id,decomposition_revision,line_number,lifecycle_status,root_net_demand_id,delivery_mode,supply_type_snapshot,sku_snapshot,gross_qty,net_qty)
            values(@otherNet,@otherGross,1,1,0,@otherNet,0,'采购','OTHER-SKU',2,2);
            insert into public.purchase_order_head(uid,origin_kind,confirmation_status,lifecycle_status,order_no,customer_id,customer_code,currency,supplier_id,supplier_code,execution_request_id,order_date)
            values(@purchase,1,0,0,@PurchaseNo,@customerId,@CustomerCode,'CNY',@supplierId,'SUP',gen_random_uuid(),'2026-09-02'),
                  (@independent,2,0,0,@IndependentNo,@customerId,@CustomerCode,'CNY',@supplierId,'SUP',gen_random_uuid(),'2026-09-02');
            insert into public.purchase_order_line(uid,purchase_order_id,line_number,order_no,sku,unit_snapshot,quantity)
            values(@purchaseLine,@purchase,1,@PurchaseNo,@ConfirmedSku,'PCS',10),
                  (@independentLine,@independent,1,@IndependentNo,@ConfirmedSku,'PCS',10);
            insert into public.demand_execution_link(uid,net_demand_id,execution_request_id,command_item_id,target_type,target_head_id,target_no,target_line_id,link_status,allocated_qty,released_qty)
            values(@ownLink,@net,gen_random_uuid(),gen_random_uuid(),'PO',@purchase,@PurchaseNo,@purchaseLine,0,6,0),
                  (@sharedLink,@otherNet,gen_random_uuid(),gen_random_uuid(),'PO',@purchase,@PurchaseNo,@purchaseLine,0,2,0);
            insert into public.supplier_progress_report(uid,purchase_order_line_id,sequence,arrangement_status,ready_qty,expected_ready_date,supplier_reported_date,is_correction,command_id)
            values(gen_random_uuid(),@purchaseLine,1,2,8,'2026-10-01','2026-09-10',FALSE,gen_random_uuid()),
                  (gen_random_uuid(),@purchaseLine,2,2,5,'2026-10-03','2026-09-11',TRUE,gen_random_uuid());
            """, new { net, demand, fingerprint, otherGross, otherNet, purchase, purchaseLine,
                independent, independentLine, ownLink, sharedLink, customerId = db.CustomerId, supplierId = db.SupplierId });
        fulfillment = await db.InScopeAsync(p => p.GetRequiredService<ISalesFulfillmentQueryService>().GetAsync(confirmed));
        var linked = fulfillment.Data!.Lines.Single().Purchases.Single();
        Assert.AreEqual(6m, linked.LinkedQty);
        Assert.AreEqual(5m, linked.PurchaseLineReadyQty);
        Assert.IsTrue(linked.IsSharedPurchaseLine);
        Assert.AreEqual(purchase, linked.PurchaseOrderId);
        await db.ExecuteAsync("""
            update public.product set unit='KG',sku=@ConfirmedSku where uid=@componentId;
            update public.net_demand set product_id=@componentId where uid=@net;
            update public.purchase_order_line set unit_snapshot='KG' where uid=@purchaseLine;
            """, new { componentId = db.ComponentId, net, purchaseLine });
        fulfillment = await db.InScopeAsync(p => p.GetRequiredService<ISalesFulfillmentQueryService>().GetAsync(confirmed));
        Assert.AreEqual(6m, fulfillment.Data!.Lines.Single().Purchases.Single().LinkedQty);
        Assert.AreEqual(string.Empty, fulfillment.Data.Lines.Single().Purchases.Single().ReviewReason);
        var childNet = Guid.NewGuid(); var childLink = Guid.NewGuid();
        await db.ExecuteAsync("""
            insert into public.net_demand(uid,gross_demand_id,source_demand_version_snapshot,source_version_snapshot,
                source_demand_basis_fingerprint_snapshot,decomposition_revision,line_number,lifecycle_status,
                root_net_demand_id,parent_net_demand_id,product_id,delivery_mode,supply_type_snapshot,sku_snapshot,gross_qty,net_qty)
            values(@childNet,@demand,(select version from public.gross_demand where uid=@demand),1,@fingerprint,
                1,2,0,@net,@net,@componentId,0,'采购',@ConfirmedSku,2,2);
            insert into public.demand_execution_link(uid,net_demand_id,execution_request_id,command_item_id,target_type,
                target_head_id,target_no,target_line_id,link_status,allocated_qty,released_qty)
            values(@childLink,@childNet,gen_random_uuid(),gen_random_uuid(),'PO',@purchase,@PurchaseNo,@purchaseLine,0,2,0);
            """, new { childNet, childLink, demand, fingerprint, net, componentId = db.ComponentId, purchase, purchaseLine });
        fulfillment = await db.InScopeAsync(p => p.GetRequiredService<ISalesFulfillmentQueryService>().GetAsync(confirmed));
        Assert.HasCount(1, fulfillment.Data!.Lines.Single().Purchases);
        Assert.AreEqual(8m, fulfillment.Data.Lines.Single().Purchases.Single().LinkedQty);
        Assert.AreEqual(5m, fulfillment.Data.Lines.Single().Purchases.Single().PurchaseLineReadyQty);
        await db.ExecuteAsync("update public.net_demand set product_id=@productId where uid=@childNet",
            new { productId = db.ProductId, childNet });
        fulfillment = await db.InScopeAsync(p => p.GetRequiredService<ISalesFulfillmentQueryService>().GetAsync(confirmed));
        Assert.HasCount(1, fulfillment.Data!.Lines.Single().Purchases);
        Assert.IsNull(fulfillment.Data.Lines.Single().Purchases.Single().LinkedQty);
        StringAssert.Contains(fulfillment.Data.Lines.Single().Purchases.Single().ReviewReason, "单位");
        await db.ExecuteAsync("update public.demand_execution_link set released_qty=allocated_qty,link_status=8 where uid=@childLink", new { childLink });
        await db.ExecuteAsync("update public.net_demand set product_id=null where uid=@net", new { net });
        await db.ExecuteAsync("update public.purchase_order_line set unit_snapshot=null where uid=@purchaseLine", new { purchaseLine });
        fulfillment = await db.InScopeAsync(p => p.GetRequiredService<ISalesFulfillmentQueryService>().GetAsync(confirmed));
        Assert.IsNull(fulfillment.Data!.Lines.Single().Purchases.Single().LinkedQty);
        StringAssert.Contains(fulfillment.Data.Lines.Single().Purchases.Single().ReviewReason, "单位");
        await db.ExecuteAsync("update public.purchase_order_line set unit_snapshot='PCS' where uid=@purchaseLine", new { purchaseLine });
        await db.ExecuteAsync("update public.demand_execution_link set released_qty=allocated_qty,link_status=8 where uid=@ownLink", new { ownLink });
        fulfillment = await db.InScopeAsync(p => p.GetRequiredService<ISalesFulfillmentQueryService>().GetAsync(confirmed));
        Assert.AreEqual(0, fulfillment.Data!.Lines.Single().Purchases.Count);
        await db.ExecuteAsync("""
            insert into public.demand_execution_link(uid,net_demand_id,execution_request_id,command_item_id,target_type,target_head_id,target_no,target_line_id,link_status,allocated_qty,released_qty)
            values(gen_random_uuid(),@net,gen_random_uuid(),gen_random_uuid(),'PO',gen_random_uuid(),@MissingNo,gen_random_uuid(),0,1,0)
            """, new { net });
        fulfillment = await db.InScopeAsync(p => p.GetRequiredService<ISalesFulfillmentQueryService>().GetAsync(confirmed));
        Assert.AreEqual("需核实", fulfillment.Data!.Lines.Single().PurchaseState);
        StringAssert.Contains(fulfillment.Data.Lines.Single().PurchaseReviewReason, "证据异常");
        var secondPacking = Guid.NewGuid(); var secondLine = Guid.NewGuid();
        await db.ExecuteAsync("""
            insert into public.packing_list_head(uid,packing_list_no,customer_id,invoice_date,lifecycle_status,posting_status,posting_date,posted_date,create_command_id)
            values(@secondPacking,@SecondPackingNo,@customerId,'2026-09-02',0,1,'2026-09-02','2026-09-02',gen_random_uuid());
            insert into public.packing_list_line(uid,packing_list_head_id,line_number,gross_demand_id,is_non_standard_snapshot,source_type_snapshot,source_document_no_snapshot,
                                         source_line_number_snapshot,sku_snapshot,unit_snapshot,qty)
            values(@secondLine,@secondPacking,1,@demand,TRUE,'SO',@ConfirmedNo,1,@ConfirmedSku,'PCS',3);
            """, new { secondPacking, secondLine, demand, customerId = db.CustomerId });
        Assert.AreEqual(7m, (await Single(new() { QuickView = SalesOrderManagerQuickView.PartiallyShipped })).TotalShippedQty);
        fulfillment = await db.InScopeAsync(p => p.GetRequiredService<ISalesFulfillmentQueryService>().GetAsync(confirmed));
        Assert.AreEqual(2, fulfillment.Data!.Lines.Single().Deliveries.Count);
        Assert.AreEqual(7m, fulfillment.Data.Lines.Single().ShippedQty);
        async Task<int> CountAt(SalesOrderManagerSearchRequest request, DateTime today)
        {
            request.CustomerId = db.CustomerId;
            var page = await db.InScopeAsync(p => p.GetRequiredService<ISalesOrderQueryRepository>()
                .SearchManagerAsync(request, today));
            return page.TotalCount;
        }
        Assert.AreEqual(1, await CountAt(new() { QuickView = SalesOrderManagerQuickView.DueSoon }, new DateTime(2026, 9, 25)));
        Assert.AreEqual(0, await CountAt(new() { QuickView = SalesOrderManagerQuickView.DueSoon }, new DateTime(2026, 9, 24)));
        Assert.AreEqual(1, await CountAt(new() { QuickView = SalesOrderManagerQuickView.Overdue }, new DateTime(2026, 10, 3)));
        Assert.AreEqual(1, await CountAt(new() { EffectiveDeliveryDateFrom = new DateTime(2026, 10, 2),
            EffectiveDeliveryDateTo = new DateTime(2026, 10, 2), CustomerSkus = ["CUSTOM-CONF"] }, new DateTime(2026, 9, 25)));
        var firstPage = await db.InScopeAsync(p => p.GetRequiredService<ISalesOrderQueryRepository>()
            .SearchManagerAsync(new() { CustomerId = db.CustomerId, PageIndex = 1, PageSize = 1 }, new DateTime(2026, 9, 25)));
        var secondPage = await db.InScopeAsync(p => p.GetRequiredService<ISalesOrderQueryRepository>()
            .SearchManagerAsync(new() { CustomerId = db.CustomerId, PageIndex = 2, PageSize = 1 }, new DateTime(2026, 9, 25)));
        Assert.AreEqual(2, firstPage.TotalCount);
        Assert.AreEqual(2, secondPage.TotalCount);
        Assert.AreNotEqual(firstPage.Items.Single().Uid, secondPage.Items.Single().Uid);
        await db.ExecuteAsync("update public.packing_list_head set posting_status=8,reversed_date='2026-09-03' where uid=@packing", new { packing });
        Assert.AreEqual(3m, (await Single(new() { QuickView = SalesOrderManagerQuickView.PartiallyShipped })).TotalShippedQty);
        await db.ExecuteAsync("update public.packing_list_head set posting_status=8,reversed_date='2026-09-03' where uid=@secondPacking", new { secondPacking });
        var reversed = await Single(new() { QuickView = SalesOrderManagerQuickView.NotShipped, InvoiceNo = "INV-CONFIRMED" });
        Assert.AreEqual(0m, reversed.TotalShippedQty);
        Assert.AreEqual(10m, reversed.TotalUnshippedQty);
        fulfillment = await db.InScopeAsync(p => p.GetRequiredService<ISalesFulfillmentQueryService>().GetAsync(confirmed));
        Assert.AreEqual(0m, fulfillment.Data!.Lines.Single().ShippedQty);
        Assert.IsTrue(fulfillment.Data.Lines.Single().Deliveries.All(row => !row.CountsTowardDelivery));
        await db.ExecuteAsync("update public.packing_list_head set posting_status=1,reversed_date=null where uid=@packing; update public.packing_list_line set qty=12 where uid=@packingLine", new { packing, packingLine });
        var overage = await Single(new() { OrderNo = "SO-CONFIRMED" });
        Assert.AreEqual(12m, overage.TotalShippedQty);
        Assert.AreEqual(0m, overage.TotalUnshippedQty);
        Assert.IsTrue(overage.NeedsReview);
        Assert.IsNull(overage.FulfillmentStatus);
        Assert.AreEqual(0, await CountAt(new() { QuickView = SalesOrderManagerQuickView.FullyShipped }, new DateTime(2026, 9, 25)));
        Assert.AreEqual(confirmed, (await Single(new() { QuickView = SalesOrderManagerQuickView.NeedsReview })).Uid);
        await db.ExecuteAsync("update public.gross_demand set demand_basis_fingerprint=null where uid=@demand", new { demand });
        var legacy = await Single(new() { QuickView = SalesOrderManagerQuickView.NeedsReview });
        Assert.IsNull(legacy.TotalShippedQty);
        var legacyDetail = await db.InScopeAsync(p => p.GetRequiredService<ISalesOrderQueryService>().GetAsync(confirmed));
        Assert.IsTrue(legacyDetail.Success, legacyDetail.Message);
        Assert.IsNull(legacyDetail.Data!.Lines.Single().ShippedQty);
        fulfillment = await db.InScopeAsync(p => p.GetRequiredService<ISalesFulfillmentQueryService>().GetAsync(confirmed));
        Assert.IsNull(fulfillment.Data!.Lines.Single().ShippedQty);
        Assert.IsNull(fulfillment.Data.Lines.Single().GrossDemandId);
        await db.ExecuteAsync("update public.gross_demand set demand_basis_fingerprint=@fingerprint where uid=@demand; update public.packing_list_head set posting_status=8,reversed_date='2026-09-03' where uid=@packing; update public.sales_order_header set document_status=9 where uid=@confirmed", new { demand, packing, confirmed, fingerprint });
        var closed = await Single(new() { QuickView = SalesOrderManagerQuickView.ClosedWithOutstanding });
        Assert.AreEqual(10m, closed.TotalUnshippedQty);
        fulfillment = await db.InScopeAsync(p => p.GetRequiredService<ISalesFulfillmentQueryService>().GetAsync(confirmed));
        Assert.AreEqual(10m, fulfillment.Data!.Lines.Single().UnshippedQty);
        Assert.AreEqual(0m, fulfillment.Data.Lines.Single().AllowedFurtherDeliveryQty);
        var empty = Guid.NewGuid();
        await db.ExecuteAsync("insert into public.sales_order_header(uid,document_status,confirmation_status,order_no,customer_id,customer_code,lead_time_days) values(@empty,0,0,@EmptyNo,@customerId,@CustomerCode,7)", new { empty, customerId = db.CustomerId });
        var emptyItem = await Single(new() { QuickView = SalesOrderManagerQuickView.NeedsReview });
        Assert.AreEqual(empty, emptyItem.Uid);
        Assert.IsFalse(emptyItem.HasUndatedOpenLine);
        var mixed = Guid.NewGuid(); var mixedLineA = Guid.NewGuid(); var mixedLineB = Guid.NewGuid();
        await db.ExecuteAsync("""
            insert into public.sales_order_header(uid,document_status,confirmation_status,order_no,customer_id,customer_code,lead_time_days)
            values(@mixed,0,0,@MixedNo,@customerId,@CustomerCode,7);
            insert into public.sales_order_line(uid,line_number,sales_order_id,is_non_standard,sku,qty,unit_snapshot)
            values(@mixedLineA,1,@mixed,TRUE,'MIX-A',2,'PCS'),(@mixedLineB,2,@mixed,TRUE,'MIX-B',3,'BOX');
            """, new { mixed, mixedLineA, mixedLineB, customerId = db.CustomerId });
        var mixedItem = await Single(new() { OrderNo = "SO-MIXED" });
        Assert.IsNull(mixedItem.TotalQty);
        Assert.IsNull(mixedItem.TotalShippedQty);
        Assert.IsFalse(mixedItem.NeedsReview);
        await db.ExecuteAsync("update public.gross_demand set unit_snapshot='BOX' where uid=@demand; update public.sales_order_header set document_status=0 where uid=@confirmed", new { demand, confirmed });
        var mismatch = await Single(new() { QuickView = SalesOrderManagerQuickView.NeedsReview, OrderNo = "SO-CONFIRMED" });
        Assert.IsNull(mismatch.TotalShippedQty);
        Assert.AreEqual(0, await CountAt(new() { QuickView = SalesOrderManagerQuickView.Overdue, OrderNo = "SO-CONFIRMED" }, new DateTime(2026, 10, 3)));
        await db.ExecuteAsync("update public.gross_demand set unit_snapshot='PCS',source_no=@WrongNo where uid=@demand", new { demand });
        var wrongSource = await Single(new() { QuickView = SalesOrderManagerQuickView.NeedsReview, OrderNo = "SO-CONFIRMED" });
        StringAssert.Contains(wrongSource.ReviewReason, "来源身份");
        await db.ExecuteAsync("""
            update public.gross_demand set source_no=@ConfirmedNo where uid=@demand;
            update public.packing_list_head set posting_status=1,reversed_date=null where uid=@packing;
            update public.packing_list_line set source_document_no_snapshot=@WrongNo where uid=@packingLine;
            """, new { demand, packing, packingLine });
        var badHistory = await Single(new() { QuickView = SalesOrderManagerQuickView.NeedsReview, OrderNo = "SO-CONFIRMED" });
        Assert.IsNull(badHistory.TotalShippedQty);
        StringAssert.Contains(badHistory.ReviewReason, "装箱过账历史");
        await db.ExecuteAsync("""
            insert into public.gross_demand(uid,lifecycle_status,source_type,source_uid,source_no,source_line_id,source_line_number,source_version,
                                    demand_basis_fingerprint,customer_id,is_non_standard,sku_snapshot,customer_sku_snapshot,unit_snapshot,demand_qty,required_date)
            values(gen_random_uuid(),9,'SO',@draft,@DraftNo,@draftLine,1,1,repeat('A',64),@customerId,TRUE,'DRAFT-SKU','CUSTOM-DRAFT','PCS',10,'2026-10-01');
            """, new { draft, draftLine, customerId = db.CustomerId });
        var pendingWithHistory = await Single(new() { QuickView = SalesOrderManagerQuickView.NeedsReview, OrderNo = "SO-DRAFT" });
        Assert.IsNull(pendingWithHistory.TotalShippedQty);
        StringAssert.Contains(pendingWithHistory.ReviewReason, "待确认仍有关联来源");
    }
    private static readonly Dictionary<string, (Guid[] Ids, string Snapshot)> OriginalRows = [];
    private static long OriginalSequence;

    /// <summary>冻结原50表身份及完整摘要；其他活动事务存在时停止，不初始化或复位。</summary>
    [ClassInitialize]
    public static async Task CaptureOriginalRowsAsync(TestContext context)
    {
        using var session = new DbSession(new PostgreSqlConnectionFactory(PostgreSqlIntegrationEnvironment.GetFailureConnectionString()));
        Assert.AreEqual(0, await session.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM pg_stat_activity WHERE datname=current_database() AND pid<>pg_backend_pid() AND backend_type='client backend' AND (state<>'idle' OR xact_start IS NOT NULL)"), "隔离写入必须串行。");
        var tables = (await session.Connection.QueryAsync<string>("SELECT table_name FROM information_schema.tables WHERE table_schema='public' AND table_type='BASE TABLE' ORDER BY table_name")).ToArray();
        Assert.AreEqual(50, tables.Length, "使用既有完整结构，不初始化。");
        foreach (var table in tables)
        {
            var ids = (await session.Connection.QueryAsync<Guid>($"SELECT uid FROM public.{PostgreSqlNames.Quote(table)}")).ToArray();
            OriginalRows.Add(table, (ids, await SnapshotAsync(session, table, ids)));
        }
        OriginalSequence = await session.Connection.ExecuteScalarAsync<long>("SELECT last_value FROM public.inventory_ledger_sequence");
    }

    /// <summary>保存每表旧行与新增身份，成功事实留存；旧行或库存序列改变则失败。</summary>
    [ClassCleanup]
    public static async Task VerifyOriginalRowsAsync()
    {
        if (OriginalRows.Count == 0) return;
        using var session = new DbSession(new PostgreSqlConnectionFactory(PostgreSqlIntegrationEnvironment.GetFailureConnectionString()));
        var evidence = new List<object>(); var unchanged = true;
        foreach (var (table, original) in OriginalRows)
        {
            var after = await SnapshotAsync(session, table, original.Ids);
            var parameters = new PostgreSqlParameters(); parameters.AddIds("OldIds", original.Ids);
            var added = (await session.Connection.QueryAsync<Guid>($"SELECT uid FROM public.{PostgreSqlNames.Quote(table)} WHERE NOT(uid=ANY(@OldIds)) ORDER BY uid", parameters)).ToArray();
            evidence.Add(new { Table = table, OldCount = original.Ids.Length, Before = original.Snapshot, After = after, AddedIds = added });
            unchanged &= original.Snapshot == after;
        }
        var sequence = await session.Connection.ExecuteScalarAsync<long>("SELECT last_value FROM public.inventory_ledger_sequence");
        Directory.CreateDirectory("TestResults/postgresql-migration");
        await File.WriteAllTextAsync("TestResults/postgresql-migration/p09-s02f-sales-manager-data-protection.json", JsonSerializer.Serialize(new
        {
            RecordedAt = DateTimeOffset.Now, Database = "dv_next_pg_failure_tests_20261004",
            SequenceBefore = OriginalSequence, SequenceAfter = sequence, Tables = evidence
        }, new JsonSerializerOptions { WriteIndented = true }));
        Assert.IsTrue(unchanged, "原记录被修改或删除；逐表证据已保存。");
        Assert.AreEqual(OriginalSequence, sequence, "销售管理读取场景不能产生库存事实。");
    }

    /// <summary>按原身份数组计算完整行摘要；表名来自本库目录并进行引用。</summary>
    private static async Task<string> SnapshotAsync(DbSession session, string table, Guid[] ids)
    {
        var parameters = new PostgreSqlParameters(); parameters.AddIds("OldIds", ids);
        return await session.Connection.ExecuteScalarAsync<string>($"""
            SELECT count(*)::text||':'||md5(COALESCE(string_agg(md5(to_jsonb(t)::text),'' ORDER BY uid),''))
            FROM public.{PostgreSqlNames.Quote(table)} t WHERE uid=ANY(@OldIds)
            """, parameters) ?? throw new AssertFailedException("原记录保护摘要为空。");
    }


    /// <summary>只服务原销售管理方法的PG夹具，沿用当前宿主DI及真实权限，不创建账号或数据库。</summary>
    private sealed class SalesFixture : IAsyncDisposable
    {
        private readonly string connection = PostgreSqlIntegrationEnvironment.GetFailureConnectionString();
        private WebApplication? app;
        public string Batch { get; } = "PGS02F_" + Guid.NewGuid().ToString("N");
        public string CustomerCode => Batch + "_TEST-C";
        public string ConfirmedSku => Batch + "_CONF-SKU";
        public Guid CustomerId { get; } = Guid.NewGuid();
        public Guid SupplierId { get; } = Guid.NewGuid();
        public Guid ProductId { get; } = Guid.NewGuid();
        public Guid ComponentId { get; } = Guid.NewGuid();

        /// <summary>只新增本场景四条基础事实；读取dv现有管理员资格，实际读取权限仍由原服务验证。</summary>
        public async Task InitializeAsync()
        {
            using var session = new DbSession(new PostgreSqlConnectionFactory(connection));
            var actor = await session.Connection.QuerySingleAsync<SalesActor>("""
                SELECT u.uid AS "UserId",u.user_name AS "UserName" FROM public.sys_user u
                WHERE u.user_name='dv' AND u.is_active AND NOT u.is_deleted AND EXISTS(
                    SELECT 1 FROM public.sys_user_role ur JOIN public.sys_role r ON r.uid=ur.role_id
                    WHERE ur.user_id=u.uid AND NOT ur.is_deleted AND r.is_active AND NOT r.is_deleted AND r.role_code='SYSTEM_ADMIN');
                """);
            await session.Connection.ExecuteAsync("""
                INSERT INTO public.customer(uid,customer_code,customer_name,doc_relate_no,deposit_rate,lead_time_days,is_active)
                VALUES(@CustomerId,@CustomerCode,'原销售管理客户',@CustomerCode,0,0,TRUE);
                INSERT INTO public.supplier(uid,supplier_code,supplier_name,doc_relate_no,is_active)
                VALUES(@SupplierId,@SupplierCode,'原销售采购供应商',@SupplierCode,TRUE);
                INSERT INTO public.product(uid,sku,description_zh,unit,material_type,supply_type,bom_version,color_id,is_active)
                VALUES(@ProductId,@RootSku,'采购成品','PCS','成品','采购',0,@EmptyId,TRUE),
                      (@ComponentId,@ComponentSku,'客供件','PCS','客供件','采购',0,@EmptyId,TRUE);
                """, new { CustomerId, CustomerCode, SupplierId, ProductId, ComponentId, SupplierCode = Batch + "_ACTUAL",
                    RootSku = Batch + "_FG", ComponentSku = Batch + "_B", EmptyId = Guid.Empty });
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = Path.Combine(Root(), "SwiftExport.WebApi"), EnvironmentName = "Development" });
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Default"] = connection });
            builder.AddWebApiServices(); builder.Services.AddSingleton<ICurrentUser>(actor);
            app = builder.Build();
        }

        /// <summary>执行明确PG方言的本批SQL；只绑定随机身份，不在运行时翻译SQL或修改既有行。</summary>
        public async Task<int> ExecuteAsync(string sql, object? args = null)
        {
            using var session = new DbSession(new PostgreSqlConnectionFactory(connection));
            var parameters = new DynamicParameters(args);
            parameters.Add("CustomerCode", CustomerCode); parameters.Add("ConfirmedSku", ConfirmedSku);
            parameters.Add("DraftNo", Batch + "_SO-DRAFT");
            parameters.Add("ConfirmedNo", Batch + "_SO-CONFIRMED");
            parameters.Add("EmptyNo", Batch + "_SO-EMPTY");
            parameters.Add("MixedNo", Batch + "_SO-MIXED");
            parameters.Add("PackingNo", Batch + "_PL-TEST");
            parameters.Add("SecondPackingNo", Batch + "_PL-TEST-2");
            parameters.Add("PurchaseNo", Batch + "_PO-LINKED");
            parameters.Add("IndependentNo", Batch + "_PO-INDEPENDENT");
            parameters.Add("MissingNo", Batch + "_PO-MISSING");
            parameters.Add("WrongNo", Batch + "_SO-WRONG");
            return await session.Connection.ExecuteAsync(sql, parameters);
        }

        /// <summary>沿用真实独立请求作用域，查询后核实事务及租约均释放，不模拟仓储或权限。</summary>
        public async Task<T> InScopeAsync<T>(Func<IServiceProvider, Task<T>> action)
        {
            using var scope = app!.Services.CreateScope();
            var result = await action(scope.ServiceProvider);
            var session = scope.ServiceProvider.GetRequiredService<DbSession>();
            Assert.IsNull(session.Transaction); Assert.IsNull(session.CommandLease); Assert.IsNull(session.SourceDemandLease);
            return result;
        }

        /// <summary>仅关闭本夹具服务，随机新增事实保留，不清理或删除数据库。</summary>
        public async ValueTask DisposeAsync() { if (app is not null) await app.DisposeAsync(); }
    }

    /// <summary>由既有dv资格查询取得请求身份；不表示本批执行HTTP或JWT登录。</summary>
    private sealed class SalesActor : ICurrentUser
    {
        public Guid? UserId { get; set; }
        public string UserName { get; set; } = "";
        /// <summary>当前请求身份已取得，业务读取权限仍使用真实服务。</summary>
        public bool IsAuthenticated => true;
    }

    /// <summary>定位已有宿主配置，仅在内存覆盖failure连接，不修改配置文件。</summary>
    private static string Root()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "SwiftExport.WebApi", "appsettings.json"))) return directory.FullName;
        throw new DirectoryNotFoundException("无法定位 WebApi 配置。");
    }
}
