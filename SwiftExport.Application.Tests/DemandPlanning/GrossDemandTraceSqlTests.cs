using Microsoft.Extensions.DependencyInjection;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Interfaces.DemandPlanning.Sources.Query;
using SwiftExport.Application.Interfaces.Procurement;
using SwiftExport.Application.Tests.Database;
using SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;
using SwiftExport.WebApi.Configuration;
using System.Text.Json;

namespace SwiftExport.Application.Tests.DemandPlanning;

/// <summary>原来源/采购/交付追踪场景接当前PG服务，随机新事实留存，保护旧行，不初始化或清理库。</summary>
[TestClass, DoNotParallelize, TestCategory("PostgreSqlIntegration")]
public sealed class GrossDemandTracePostgreSqlTests
{
    /// <summary>验证可信拆解和活动 Link 的贡献，取消后仍保留历史；旧来源版本不推断当前采购量。</summary>
    [TestMethod]
    public async Task MaterialAndPurchaseTrace_RespectSourceVersionAndLinkRelease()
    {
        await using var db = new TraceFixture();
        await db.InitializeAsync();
        var demand = Guid.NewGuid(); var root = Guid.NewGuid(); var material = Guid.NewGuid();
        var order = Guid.NewGuid(); var line = Guid.NewGuid(); var link = Guid.NewGuid();
        var posted = Guid.NewGuid(); var receipt = Guid.NewGuid();
        const string fingerprint = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
        await db.ExecuteAsync("UPDATE public.product SET unit='KG' WHERE uid=@componentId", new { componentId = db.ComponentId });
        await db.ExecuteAsync("""
            INSERT INTO public.gross_demand(uid,lifecycle_status,source_type,source_uid,source_no,source_line_id,
                                    customer_id,demand_basis_fingerprint,is_non_standard,sku_snapshot,unit_snapshot,demand_qty)
            VALUES(@demand,0,'MD',@demand,@BatchTraceNo,@demand,@customerId,@fingerprint,TRUE,'SKU','PCS',10);
            INSERT INTO public.net_demand(uid,gross_demand_id,source_demand_version_snapshot,source_demand_basis_fingerprint_snapshot,
                                 decomposition_revision,line_number,lifecycle_status,root_net_demand_id,parent_net_demand_id,
                                 product_id,delivery_mode,supply_type_snapshot,sku_snapshot,unit_snapshot,gross_qty,net_qty)
            VALUES(@root,@demand,(SELECT version FROM public.gross_demand WHERE uid=@demand),@fingerprint,1,1,0,@root,NULL,@productId,0,'采购','SKU','PCS',10,10),
                  (@material,@demand,(SELECT version FROM public.gross_demand WHERE uid=@demand),@fingerprint,1,2,0,@root,@root,@componentId,0,'采购','TEST-B','KG',10,10);
            INSERT INTO public.purchase_order_head(uid,origin_kind,confirmation_status,lifecycle_status,order_no,customer_id,
                                          customer_code,currency,supplier_id,supplier_code,execution_request_id,order_date)
            VALUES(@order,1,0,0,@BatchOrderNo,@customerId,'TEST-C','CNY',@supplierId,'SUP',gen_random_uuid(),'2026-09-02');
            INSERT INTO public.purchase_order_line(uid,purchase_order_id,line_number,order_no,sku,unit_snapshot,quantity)
            VALUES(@line,@order,1,@BatchOrderNo,'TEST-B','KG',10);
            INSERT INTO public.demand_execution_link(uid,net_demand_id,execution_request_id,command_item_id,target_type,
                                             target_head_id,target_no,target_line_id,link_status,allocated_qty,released_qty)
            VALUES(@link,@material,gen_random_uuid(),gen_random_uuid(),'PO',@order,@BatchOrderNo,@line,0,6,0);
            """, new { demand, root, material, order, line, link, customerId = db.CustomerId,
                supplierId = db.SupplierId, productId = db.ProductId, componentId = db.ComponentId, fingerprint });
        await db.ExecuteAsync("""

            INSERT INTO public.inbound_document(uid,document_no,document_type,creation_mode,lifecycle_status,posting_status,warehouse_site_id,receipt_date)
            VALUES(@posted,@BatchReceiptNo,10,0,0,1,@warehouseId,'2026-09-03');
            INSERT INTO public.inbound_document_line(uid,inbound_document_id,line_number,product_id,sku_snapshot,unit_snapshot,quantity,source_line_id)
            VALUES(gen_random_uuid(),@posted,1,@productId,'TEST-B','KG',3,@line);
            """, new { line, warehouseId = db.ReceiptSiteId, productId = db.ComponentId, posted });
        var trace = await db.InScopeAsync(p => p.GetRequiredService<IGrossDemandQueryService>().GetTraceAsync(demand));
        Assert.IsTrue(trace.Success, trace.Message);
        Assert.AreEqual(6m, trace.Data!.Materials.Single(row => row.NetDemandId == material).ActivePurchaseQty);
        Assert.AreEqual(6m, trace.Data.Purchases.Single().LinkedQty);
        Assert.AreEqual(3m, trace.Data.Purchases.Single().PurchaseLineReceivedQty);
        var purchaseTrace = await db.InScopeAsync(p => p.GetRequiredService<IPurchaseOrderTraceService>().GetAsync(order));
        Assert.IsTrue(purchaseTrace.Success, purchaseTrace.Message);
        Assert.IsFalse(purchaseTrace.Data!.Lines.Single().Links.Single().NeedsReview);
        await db.ExecuteAsync("UPDATE public.purchase_order_line SET unit_snapshot='PCS' WHERE uid=@line", new { line });
        trace = await db.InScopeAsync(p => p.GetRequiredService<IGrossDemandQueryService>().GetTraceAsync(demand));
        Assert.IsNull(trace.Data!.Materials.Single(row => row.NetDemandId == material).ActivePurchaseQty);
        StringAssert.Contains(trace.Data.Purchases.Single().ReviewReason, "单位");
        purchaseTrace = await db.InScopeAsync(p => p.GetRequiredService<IPurchaseOrderTraceService>().GetAsync(order));
        Assert.IsTrue(purchaseTrace.Success, purchaseTrace.Message);
        Assert.IsTrue(purchaseTrace.Data!.Lines.Single().Links.Single().NeedsReview);
        await db.ExecuteAsync("UPDATE public.purchase_order_line SET unit_snapshot='KG' WHERE uid=@line", new { line });
        var hasPurchase = await db.InScopeAsync(p => p.GetRequiredService<IGrossDemandQueryService>()
            .SearchAsync(new GrossDemandSearchRequest { TraceView = "HasPurchase", Keyword = db.Batch }));
        Assert.AreEqual(1, hasPurchase.Data!.TotalCount);
        Assert.IsTrue(hasPurchase.Data.Items.Single().HasValidPurchase);
        await db.ExecuteAsync("UPDATE public.gross_demand SET demand_basis_fingerprint=NULL WHERE uid=@demand", new { demand });
        var unknownPurchase = await db.InScopeAsync(p => p.GetRequiredService<IGrossDemandQueryService>()
            .SearchAsync(new GrossDemandSearchRequest { TraceView = "HasPurchase", Keyword = db.Batch }));
        Assert.AreEqual(0, unknownPurchase.Data!.TotalCount);
        await db.ExecuteAsync("UPDATE public.gross_demand SET demand_basis_fingerprint=@fingerprint WHERE uid=@demand",
            new { demand, fingerprint });

        await db.ExecuteAsync("UPDATE public.demand_execution_link SET released_qty=allocated_qty,link_status=8 WHERE uid=@link", new { link });
        trace = await db.InScopeAsync(p => p.GetRequiredService<IGrossDemandQueryService>().GetTraceAsync(demand));
        Assert.AreEqual(0m, trace.Data!.Materials.Single(row => row.NetDemandId == material).ActivePurchaseQty);
        Assert.AreEqual(0m, trace.Data.Purchases.Single().LinkedQty);
        StringAssert.Contains(trace.Data.Purchases.Single().ReviewReason, "历史");
        var noPurchase = await db.InScopeAsync(p => p.GetRequiredService<IGrossDemandQueryService>()
            .SearchAsync(new GrossDemandSearchRequest { TraceView = "NoPurchase", Keyword = db.Batch }));
        Assert.AreEqual(1, noPurchase.Data!.TotalCount);

        var orphan = Guid.NewGuid();
        await db.ExecuteAsync("""
            INSERT INTO public.demand_execution_link(uid,net_demand_id,execution_request_id,command_item_id,target_type,
                                             target_head_id,target_no,target_line_id,link_status,allocated_qty,released_qty)
            VALUES(@orphan,@material,gen_random_uuid(),gen_random_uuid(),'PO',gen_random_uuid(),@BatchMissingNo,gen_random_uuid(),0,2,0)
            """, new { orphan, material });
        trace = await db.InScopeAsync(p => p.GetRequiredService<IGrossDemandQueryService>().GetTraceAsync(demand));
        Assert.IsNull(trace.Data!.Materials.Single(row => row.NetDemandId == material).ActivePurchaseQty);
        StringAssert.Contains(trace.Data.Materials.Single(row => row.NetDemandId == material).ReviewReason, "目标");
        Assert.IsFalse(trace.Data.Purchases.Single(row => row.LinkedQty == 2m).HasPurchaseTarget);
        await db.ExecuteAsync("UPDATE public.demand_execution_link SET released_qty=allocated_qty,link_status=8 WHERE uid=@orphan", new { orphan });

        await db.ExecuteAsync("UPDATE public.gross_demand SET demand_basis_fingerprint=NULL WHERE uid=@demand", new { demand });
        trace = await db.InScopeAsync(p => p.GetRequiredService<IGrossDemandQueryService>().GetTraceAsync(demand));
        Assert.IsNull(trace.Data!.Materials.Single(row => row.NetDemandId == material).ActivePurchaseQty);
        StringAssert.Contains(trace.Data.Materials.Single(row => row.NetDemandId == material).ReviewReason, "需核实");

        // 无关历史不阻断本来源；相关反向缺少流水证明时整行实收保持未知。
        await db.ExecuteAsync("""
            INSERT INTO public.inbound_document(uid,document_no,document_type,creation_mode,lifecycle_status,posting_status,warehouse_site_id,receipt_date)
            VALUES(gen_random_uuid(),@BatchUnrelatedNo,10,0,0,8,@warehouseId,'2026-09-03');
            """, new { warehouseId = db.ReceiptSiteId });
        var unaffected = await db.InScopeAsync(p => p.GetRequiredService<IGrossDemandQueryService>().GetTraceAsync(demand));
        Assert.IsTrue(unaffected.Success, unaffected.Message);
        await db.ExecuteAsync("""

            INSERT INTO public.inbound_document(uid,document_no,document_type,creation_mode,lifecycle_status,posting_status,warehouse_site_id,receipt_date)
            VALUES(@receipt,@BatchRelatedNo,10,0,0,8,@warehouseId,'2026-09-03');
            INSERT INTO public.inbound_document_line(uid,inbound_document_id,line_number,product_id,sku_snapshot,unit_snapshot,quantity,source_line_id)
            VALUES(gen_random_uuid(),@receipt,1,@productId,'TEST-B','KG',2,@line);
            """, new { warehouseId = db.ReceiptSiteId, productId = db.ComponentId, line, receipt });
        var uncertain = await db.InScopeAsync(p => p.GetRequiredService<IGrossDemandQueryService>().GetTraceAsync(demand));
        Assert.IsFalse(uncertain.Success);
        StringAssert.Contains(uncertain.Message, "收货量未知");
    }

    /// <summary>筛选先于分页；已过账数量可累计，反向行只保留为历史。</summary>
    [TestMethod]
    public async Task TraceAndFilters_UseCurrentLinksAndPostedDelivery()
    {
        await using var db = new TraceFixture();
        await db.InitializeAsync(purchase: false);
        var first = Guid.NewGuid(); var second = Guid.NewGuid();
        var posted = Guid.NewGuid(); var reversed = Guid.NewGuid();
        const string fingerprint = "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";
        await db.ExecuteAsync("""
            INSERT INTO public.gross_demand(uid,lifecycle_status,source_type,source_uid,source_no,source_line_id,customer_id,
                                    demand_basis_fingerprint,is_non_standard,sku_snapshot,unit_snapshot,demand_qty)
            VALUES(@first,0,'MD',@first,@BatchFirstNo,@first,@customerId,@fingerprint,TRUE,'SKU-1','PCS',10),
                  (@second,0,'MD',@second,@BatchSecondNo,@second,@customerId,@fingerprint,TRUE,'SKU-2','PCS',5);
            INSERT INTO public.packing_list_head(uid,packing_list_no,customer_id,invoice_date,lifecycle_status,posting_status,posted_date,reversed_date,create_command_id)
            VALUES(@posted,@BatchPostedNo,@customerId,'2026-09-01',0,1,'2026-09-02',NULL,gen_random_uuid()),
                  (@reversed,@BatchReversedNo,@customerId,'2026-09-01',0,8,'2026-09-02','2026-09-03',gen_random_uuid());
            INSERT INTO public.packing_list_line(uid,packing_list_head_id,line_number,gross_demand_id,is_non_standard_snapshot,
                                        source_type_snapshot,source_document_no_snapshot,sku_snapshot,unit_snapshot,qty)
            VALUES(gen_random_uuid(),@posted,1,@first,TRUE,'MD',@BatchFirstNo,'SKU-1','PCS',4),
                  (gen_random_uuid(),@reversed,1,@first,TRUE,'MD',@BatchFirstNo,'SKU-1','PCS',3);
            """, new { first, second, posted, reversed, customerId = db.CustomerId, fingerprint });
        var trace = await db.InScopeAsync(p => p.GetRequiredService<IGrossDemandQueryService>().GetTraceAsync(first));
        Assert.IsTrue(trace.Success, trace.Message);
        Assert.AreEqual(4m, trace.Data!.DeliveredQty);
        Assert.HasCount(2, trace.Data.Deliveries);
        Assert.AreEqual(1, trace.Data.Deliveries.Count(x => x.CountsTowardDelivery));
        await db.ExecuteAsync("UPDATE public.packing_list_line SET sku_snapshot='WRONG' WHERE packing_list_head_id=@posted", new { posted });
        var wrongSnapshot = await db.InScopeAsync(p => p.GetRequiredService<IGrossDemandQueryService>().GetTraceAsync(first));
        Assert.IsTrue(wrongSnapshot.Success, wrongSnapshot.Message);
        Assert.IsNull(wrongSnapshot.Data!.DeliveredQty);
        StringAssert.Contains(wrongSnapshot.Data.ReviewReason, "装箱历史");
        await db.ExecuteAsync("UPDATE public.packing_list_line SET sku_snapshot='SKU-1' WHERE packing_list_head_id=@posted", new { posted });
        await db.ExecuteAsync("UPDATE public.gross_demand SET demand_basis_fingerprint=NULL WHERE uid=@first", new { first });
        var unverified = await db.InScopeAsync(p => p.GetRequiredService<IGrossDemandQueryService>().GetTraceAsync(first));
        Assert.IsTrue(unverified.Success, unverified.Message);
        Assert.AreEqual(4m, unverified.Data!.DeliveredQty);
        StringAssert.Contains(unverified.Data.ReviewReason, "指纹");
        await db.ExecuteAsync("UPDATE public.gross_demand SET demand_basis_fingerprint=@fingerprint WHERE uid=@first", new { first, fingerprint });

        var hasDelivery = await db.InScopeAsync(p => p.GetRequiredService<IGrossDemandQueryService>()
            .SearchAsync(new GrossDemandSearchRequest { TraceView = "HasDelivery", Keyword = db.Batch, PageIndex = 1, PageSize = 1 }));
        Assert.IsTrue(hasDelivery.Success, hasDelivery.Message);
        Assert.AreEqual(1, hasDelivery.Data!.TotalCount);
        Assert.AreEqual(first, hasDelivery.Data.Items.Single().Uid);
        Assert.IsTrue(hasDelivery.Data.Items.Single().HasPostedDelivery);
        Assert.IsFalse(hasDelivery.Data.Items.Single().HasActiveMaterial);
        var noDelivery = await db.InScopeAsync(p => p.GetRequiredService<IGrossDemandQueryService>()
            .SearchAsync(new GrossDemandSearchRequest { TraceView = "NoDelivery", Keyword = db.Batch, PageIndex = 1, PageSize = 1 }));
        Assert.AreEqual(1, noDelivery.Data!.TotalCount);
        Assert.AreEqual(second, noDelivery.Data.Items.Single().Uid);
        var customer = await db.InScopeAsync(p => p.GetRequiredService<IGrossDemandQueryService>()
            .SearchAsync(new GrossDemandSearchRequest { CustomerKeyword = db.CustomerCode, PageIndex = 1, PageSize = 1 }));
        Assert.AreEqual(2, customer.Data!.TotalCount);
        Assert.HasCount(1, customer.Data.Items);

        await db.ExecuteAsync("""
            UPDATE public.gross_demand SET source_type='SO',source_version=NULL WHERE uid=@first;
            UPDATE public.packing_list_line SET source_type_snapshot='SO' WHERE gross_demand_id=@first;
            """, new { first });
        var legacySource = await db.InScopeAsync(p => p.GetRequiredService<IGrossDemandQueryService>().GetTraceAsync(first));
        Assert.IsTrue(legacySource.Success, legacySource.Message);
        Assert.AreEqual(4m, legacySource.Data!.DeliveredQty);
        StringAssert.Contains(legacySource.Data.ReviewReason, "版本");
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
        await File.WriteAllTextAsync("TestResults/postgresql-migration/p09-s02d-gross-trace-data-protection.json", JsonSerializer.Serialize(new
        {
            RecordedAt = DateTimeOffset.Now, Database = "dv_next_pg_failure_tests_20261004",
            SequenceBefore = OriginalSequence, SequenceAfter = sequence, Tables = evidence
        }, new JsonSerializerOptions { WriteIndented = true }));
        Assert.IsTrue(unchanged, "原记录被修改或删除；逐表证据已保存。");
        Assert.AreEqual(OriginalSequence, sequence, "来源采购交付追踪不能产生库存事实。");
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

    /// <summary>只服务原两个追踪场景的当前PG夹具；不创建库、表、账号或公共SQL翻译器。</summary>
    private sealed class TraceFixture : IAsyncDisposable
    {
        private readonly string connection = PostgreSqlIntegrationEnvironment.GetFailureConnectionString();
        private WebApplication? app;
        public string Batch { get; } = "PGS02D_" + Guid.NewGuid().ToString("N");
        public string CustomerCode => Batch + "_TEST-C";
        public Guid CustomerId { get; } = Guid.NewGuid();
        public Guid SupplierId { get; } = Guid.NewGuid();
        public Guid ReceiptSupplierId { get; } = Guid.NewGuid();
        public Guid ProductId { get; } = Guid.NewGuid();
        public Guid ComponentId { get; } = Guid.NewGuid();
        public Guid ReceiptSiteId { get; } = Guid.NewGuid();

        /// <summary>只新增本方法实际所需事实，读取dv现有管理员资格，以当前宿主DI执行原读取权限。</summary>
        public async Task InitializeAsync(bool purchase = true)
        {
            using var session = new DbSession(new PostgreSqlConnectionFactory(connection));
            var actor = await session.Connection.QuerySingleAsync<TraceActor>("""
                SELECT u.uid AS "UserId",u.user_name AS "UserName" FROM public.sys_user u
                WHERE u.user_name='dv' AND u.is_active AND NOT u.is_deleted AND EXISTS(
                    SELECT 1 FROM public.sys_user_role ur JOIN public.sys_role r ON r.uid=ur.role_id
                    WHERE ur.user_id=u.uid AND NOT ur.is_deleted AND r.is_active AND NOT r.is_deleted AND r.role_code='SYSTEM_ADMIN');
                """);
            await session.Connection.ExecuteAsync("""
                INSERT INTO public.customer(uid,customer_code,customer_name,doc_relate_no,deposit_rate,lead_time_days,is_active)
                VALUES(@CustomerId,@CustomerCode,'原来源追踪客户',@CustomerCode,0,0,TRUE);
                """, new { CustomerId, CustomerCode });
            if (purchase)
                await session.Connection.ExecuteAsync("""
                    INSERT INTO public.supplier(uid,supplier_code,supplier_name,doc_relate_no,is_active)
                    VALUES(@SupplierId,@ActualCode,'实际供应商',@ActualCode,TRUE),
                          (@ReceiptSupplierId,@ReceiptCode,'收货供应商',@ReceiptCode,TRUE);
                    INSERT INTO public.product(uid,sku,description_zh,unit,material_type,supply_type,bom_version,color_id,is_active)
                    VALUES(@ProductId,@RootSku,'采购成品','PCS','成品','采购',0,@EmptyId,TRUE),
                          (@ComponentId,@ComponentSku,'客供件','PCS','客供件','采购',0,@EmptyId,TRUE);
                    INSERT INTO public.warehouse_site(uid,code,name,site_type,supplier_id,is_active)
                    VALUES(@ReceiptSiteId,@SiteCode,'独立新收货仓',0,@ReceiptSupplierId,TRUE);
                    """, new { SupplierId, ReceiptSupplierId, ProductId, ComponentId, ReceiptSiteId, ActualCode = Batch + "_ACTUAL",
                        ReceiptCode = Batch + "_RECEIPT", RootSku = Batch + "_FG", ComponentSku = Batch + "_B", SiteCode = Batch + "_R", EmptyId = Guid.Empty });
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = Path.Combine(Root(), "SwiftExport.WebApi"), EnvironmentName = "Development" });
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Default"] = connection });
            builder.AddWebApiServices(); builder.Services.AddSingleton<ICurrentUser>(actor);
            app = builder.Build();
        }

        /// <summary>执行已明确写成PG方言的原夹具SQL，仅补绑定本批随机单号，不在运行时改SQL或旧记录。</summary>
        public async Task<int> ExecuteAsync(string sql, object? args = null)
        {
            using var session = new DbSession(new PostgreSqlConnectionFactory(connection));
            var parameters = new DynamicParameters(args);
            parameters.Add("BatchTraceNo", Batch + "_MD-TRACE"); parameters.Add("BatchOrderNo", Batch + "_PO-TRACE");
            parameters.Add("BatchMissingNo", Batch + "_PO-MISSING"); parameters.Add("BatchReceiptNo", Batch + "_IR-TRACE");
            parameters.Add("BatchUnrelatedNo", Batch + "_IR-UNVERIFIED-REVERSE"); parameters.Add("BatchRelatedNo", Batch + "_IR-RELATED-REVERSE");
            parameters.Add("BatchFirstNo", Batch + "_MD-1"); parameters.Add("BatchSecondNo", Batch + "_MD-2");
            parameters.Add("BatchPostedNo", Batch + "_PL-POSTED"); parameters.Add("BatchReversedNo", Batch + "_PL-REVERSED");
            return await session.Connection.ExecuteAsync(sql, parameters);
        }

        /// <summary>保持真实独立request作用域和原服务事务所有权，不模拟查询、权限或异常分类。</summary>
        public async Task<T> InScopeAsync<T>(Func<IServiceProvider, Task<T>> action)
        {
            using var scope = app!.Services.CreateScope();
            var result = await action(scope.ServiceProvider);
            var session = scope.ServiceProvider.GetRequiredService<DbSession>();
            Assert.IsNull(session.Transaction); Assert.IsNull(session.CommandLease); Assert.IsNull(session.SourceDemandLease);
            return result;
        }

        /// <summary>仅关闭本夹具服务和连接，所有新事实保留，不删除或清理数据。</summary>
        public async ValueTask DisposeAsync() { if (app is not null) await app.DisposeAsync(); }
    }

    /// <summary>dv现有有效管理员请求身份，权限仍由实际服务查询，不创建或改写账号。</summary>
    private sealed class TraceActor : ICurrentUser
    {
        public Guid? UserId { get; set; }
        public string UserName { get; set; } = "";
        /// <summary>身份由本库资格查询取得，不代表本批运行了HTTP/JWT登录。</summary>
        public bool IsAuthenticated => true;
    }

    /// <summary>定位宿主配置，连接仅在内存覆盖为授权failure库，不修改配置文件。</summary>
    private static string Root()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "SwiftExport.WebApi", "appsettings.json"))) return directory.FullName;
        throw new DirectoryNotFoundException("无法定位 WebApi 配置。");
    }
}
