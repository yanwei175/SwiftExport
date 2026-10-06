using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.DemandPlanning.Materials.Query;
using SwiftExport.Application.Services.DemandPlanning.Materials.Query;
using SwiftExport.Application.Tests.Database;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;
using SwiftExport.Infrastructure.Repositories.DemandPlanning.Materials.Query;
using SwiftExport.Infrastructure.Services.Auth;
using System.Text.Json;

namespace SwiftExport.Application.Tests.DemandPlanning;

/// <summary>原共享收货追踪接现有PG查询服务和仓储；仅随机新增本批事实，保护原行，不初始化或清理库。</summary>
[TestClass, DoNotParallelize, TestCategory("PostgreSqlIntegration")]
public sealed class MaterialDemandTracePostgreSqlTests
{
    /// <summary>验证筛选先于分页、共享行收货覆盖、反向和历史追踪。</summary>
    [TestMethod]
    public async Task SearchAndTrace_RespectLinkedQtyAndCurrentReceiptCoverage()
    {
        var connection = PostgreSqlIntegrationEnvironment.GetFailureConnectionString();
        using var db = new DbSession(new PostgreSqlConnectionFactory(connection));
        var batch = "PGS02C_" + Guid.NewGuid().ToString("N");
        var customerId = Guid.NewGuid(); var supplierId = Guid.NewGuid(); var receiptSupplierId = Guid.NewGuid();
        var warehouseId = Guid.NewGuid(); var productId = Guid.NewGuid(); var receipt = Guid.NewGuid();
        var demand = Guid.NewGuid(); var first = Guid.NewGuid(); var second = Guid.NewGuid();
        var order = Guid.NewGuid(); var line = Guid.NewGuid();
        var orderNo = batch + "_PO-MATERIAL";
        // 只创建原读取所需基础事实；收货供应商与实际供应商不同，独立新仓不碰既有供应商活动虚拟仓。
        await db.Connection.ExecuteAsync("""
            INSERT INTO public.customer(uid,customer_code,customer_name,doc_relate_no,deposit_rate,lead_time_days,is_active)
            VALUES(@customerId,@batch,'原共享收货',@batch,0,0,TRUE);
            INSERT INTO public.supplier(uid,supplier_code,supplier_name,doc_relate_no,is_active)
            VALUES(@supplierId,@SupplierCode,'实际供应商',@SupplierCode,TRUE),
                  (@receiptSupplierId,@ReceiptSupplierCode,'收货供应商',@ReceiptSupplierCode,TRUE);
            INSERT INTO public.product(uid,sku,description_zh,unit,material_type,supply_type,bom_version,color_id,is_active)
            VALUES(@productId,@ProductSku,'采购成品','PCS','成品','采购',0,@EmptyId,TRUE);
            INSERT INTO public.warehouse_site(uid,code,name,site_type,supplier_id,is_active)
            VALUES(@warehouseId,@WarehouseCode,'独立新收货仓',0,@receiptSupplierId,TRUE);
            INSERT INTO public.gross_demand(uid,lifecycle_status,source_type,source_uid,source_no,source_line_id,
                customer_id,demand_basis_fingerprint,is_non_standard,sku_snapshot,unit_snapshot,demand_qty)
            VALUES(@demand,0,'MD',@demand,@SourceNo,@demand,@customerId,'MATERIAL-BASIS',TRUE,'SKU','PCS',12);
            INSERT INTO public.net_demand(uid,gross_demand_id,source_demand_version_snapshot,source_demand_basis_fingerprint_snapshot,
                decomposition_revision,line_number,lifecycle_status,root_net_demand_id,delivery_mode,
                supply_type_snapshot,sku_snapshot,unit_snapshot,gross_qty,net_qty,required_date)
            VALUES(@first,@demand,(SELECT version FROM public.gross_demand WHERE uid=@demand),'MATERIAL-BASIS',
                   1,1,0,@first,0,'采购','SKU','PCS',6,6,'2026-09-30'),
                  (@second,@demand,(SELECT version FROM public.gross_demand WHERE uid=@demand),'MATERIAL-BASIS',
                   1,2,0,@second,0,'采购','SKU','PCS',6,6,'2026-10-01');
            INSERT INTO public.purchase_order_head(uid,origin_kind,confirmation_status,lifecycle_status,order_no,customer_id,
                customer_code,currency,supplier_id,supplier_code,execution_request_id,order_date)
            VALUES(@order,1,0,0,@orderNo,@customerId,@batch,'CNY',@supplierId,@SupplierCode,gen_random_uuid(),'2026-09-02');
            INSERT INTO public.purchase_order_line(uid,purchase_order_id,line_number,order_no,sku,unit_snapshot,quantity)
            VALUES(@line,@order,1,@orderNo,'SKU','PCS',12);
            INSERT INTO public.demand_execution_link(uid,net_demand_id,execution_request_id,command_item_id,target_type,
                target_head_id,target_no,target_line_id,link_status,allocated_qty,released_qty,demand_required_date)
            VALUES(gen_random_uuid(),@first,gen_random_uuid(),gen_random_uuid(),'PO',@order,@orderNo,@line,0,6,0,'2026-09-30'),
                  (gen_random_uuid(),@second,gen_random_uuid(),gen_random_uuid(),'PO',@order,@orderNo,@line,0,6,0,'2026-10-01');
            INSERT INTO public.inbound_document(uid,document_no,document_type,creation_mode,lifecycle_status,posting_status,warehouse_site_id,receipt_date)
            VALUES(@receipt,@ReceiptNo,10,0,0,1,@warehouseId,'2026-09-03');
            INSERT INTO public.inbound_document_line(uid,inbound_document_id,line_number,product_id,sku_snapshot,unit_snapshot,quantity,source_line_id)
            VALUES(gen_random_uuid(),@receipt,1,@productId,'SKU','PCS',7,@line);
            INSERT INTO public.inbound_document(uid,document_no,document_type,creation_mode,lifecycle_status,posting_status,warehouse_site_id,receipt_date)
            VALUES(gen_random_uuid(),@UnrelatedReceiptNo,10,0,0,8,@warehouseId,'2026-09-03');
            """, new { demand, first, second, order, line, customerId, supplierId, receiptSupplierId, warehouseId, productId, receipt, batch, orderNo,
                SupplierCode = batch + "_ACTUAL", ReceiptSupplierCode = batch + "_RECEIPT", ProductSku = batch + "_FG",
                WarehouseCode = batch + "_R", SourceNo = batch + "_MD-MATERIAL", ReceiptNo = batch + "_IR-MATERIAL",
                UnrelatedReceiptNo = batch + "_IR-MATERIAL-UNRELATED", EmptyId = Guid.Empty });

        // 每次读取有独立原会话/服务上下文；不借外层测试事务，也不模拟异常转换或查询仓储。
        async Task<T> Run<T>(Func<IMaterialDemandQueryService, Task<T>> action)
        {
            using var session = new DbSession(new PostgreSqlConnectionFactory(connection));
            var user = new TraceUser();
            var context = new RepositoryBaseContext(session, NullLoggerFactory.Instance, new AuditStampService(user), user);
            var service = new MaterialDemandQueryService(new ServiceBaseContext(session, NullLoggerFactory.Instance),
                new PostgreSqlMaterialDemandQueryRepository(context));
            var result = await action(service);
            Assert.IsNull(session.Transaction); Assert.IsNull(session.SourceDemandLease);
            return result;
        }

        var list = await Run(service => service
            .SearchManagerAsync(new MaterialDemandManagerSearchRequest
            { QuickView = "Assigned", PageIndex = 1, PageSize = 1, PurchaseOrderNo = orderNo }));
        Assert.IsTrue(list.Success, list.Message);
        Assert.AreEqual(2, list.Data!.TotalCount);
        Assert.HasCount(1, list.Data.Items);
        Assert.AreEqual(6m, list.Data.Items.Single().AssociatedPurchaseQty);
        var firstTrace = await Run(service => service.GetTraceAsync(first));
        var secondTrace = await Run(service => service.GetTraceAsync(second));
        Assert.IsTrue(firstTrace.Success, firstTrace.Message);
        Assert.AreEqual(6m, firstTrace.Data!.CurrentReceiptCoverageQty);
        Assert.AreEqual(1m, secondTrace.Data!.CurrentReceiptCoverageQty);
        Assert.AreEqual(7m, secondTrace.Data.Purchases.Single().PurchaseLineReceivedQty);

        await db.Connection.ExecuteAsync("UPDATE public.inbound_document SET posting_status=8 WHERE uid=@receipt", new { receipt });
        firstTrace = await Run(service => service.GetTraceAsync(first));
        Assert.IsFalse(firstTrace.Success);
        StringAssert.Contains(firstTrace.Message, "收货量未知");
        await db.Connection.ExecuteAsync("UPDATE public.inbound_document SET posting_status=1 WHERE uid=@receipt", new { receipt });
        await db.Connection.ExecuteAsync("UPDATE public.demand_execution_link SET released_qty=allocated_qty,link_status=8 WHERE net_demand_id=@first", new { first });
        await db.Connection.ExecuteAsync("UPDATE public.net_demand SET lifecycle_status=8 WHERE uid=@first", new { first });
        var history = await Run(service => service
            .SearchManagerAsync(new MaterialDemandManagerSearchRequest
            { LifecycleStatus = NetDemandLifecycleStatusEnum.RolledBack, Keyword = batch }));
        Assert.AreEqual(1, history.Data!.TotalCount);
        firstTrace = await Run(service => service.GetTraceAsync(first));
        Assert.IsNull(firstTrace.Data!.CurrentReceiptCoverageQty);
        Assert.HasCount(1, firstTrace.Data.Purchases);

        await db.Connection.ExecuteAsync("UPDATE public.gross_demand SET demand_basis_fingerprint=NULL WHERE uid=@demand", new { demand });
        var review = await Run(service => service
            .SearchManagerAsync(new MaterialDemandManagerSearchRequest { QuickView = "Review", Keyword = batch }));
        Assert.AreEqual(1, review.Data!.TotalCount);
        Assert.IsNull(review.Data.Items.Single().AssociatedPurchaseQty);
        Assert.AreEqual(string.Empty, review.Data.Items.Single().DueHint);
        await db.Connection.ExecuteAsync("UPDATE public.gross_demand SET demand_basis_fingerprint='MATERIAL-BASIS' WHERE uid=@demand", new { demand });
        await db.Connection.ExecuteAsync("UPDATE public.purchase_order_line SET unit_snapshot='BOX' WHERE uid=@line", new { line });
        review = await Run(service => service
            .SearchManagerAsync(new MaterialDemandManagerSearchRequest { QuickView = "Review", Keyword = batch }));
        Assert.AreEqual(1, review.Data!.TotalCount);
        StringAssert.Contains(review.Data.Items.Single().ReviewReason, "单位");
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
        await File.WriteAllTextAsync("TestResults/postgresql-migration/p09-s02c-material-trace-data-protection.json", JsonSerializer.Serialize(new
        {
            RecordedAt = DateTimeOffset.Now, Database = "dv_next_pg_failure_tests_20261004",
            SequenceBefore = OriginalSequence, SequenceAfter = sequence, Tables = evidence
        }, new JsonSerializerOptions { WriteIndented = true }));
        Assert.IsTrue(unchanged, "原记录被修改或删除；逐表证据已保存。");
        Assert.AreEqual(OriginalSequence, sequence, "共享收货追踪不能产生库存事实。");
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

    /// <summary>只为仓储上下文提供测试身份；原只读服务不调用认证或业务权限，本例不声称验证这两者。</summary>
    private sealed class TraceUser : ICurrentUser
    {
        public Guid? UserId { get; } = Guid.NewGuid();
        public string UserName => "pg-material-trace-test";
        /// <summary>独立测试请求身份，不创建或修改实际账号、角色和权限。</summary>
        public bool IsAuthenticated => true;
    }
}
