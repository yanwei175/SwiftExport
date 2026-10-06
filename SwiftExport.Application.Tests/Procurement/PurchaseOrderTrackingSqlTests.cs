using Microsoft.Extensions.DependencyInjection;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Interfaces.Procurement;
using SwiftExport.Application.Tests.Database;
using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderManagerDto;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;
using SwiftExport.WebApi.Configuration;
using System.Text.Json;
using Npgsql;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;

namespace SwiftExport.Application.Tests.Procurement;

/// <summary>原采购跟踪四场景各接已授权独立PG新库及真实服务；来源旧行只读保护，新库基线与事实留存，不清理任何库。</summary>
[TestClass, DoNotParallelize, TestCategory("PostgreSqlIntegration")]
public sealed class PurchaseOrderTrackingPostgreSqlTests
{
    /// <summary>待关联资格先于分页；包含部分关联及已确认采购，排除零剩余、未知、软删行和作废单。</summary>
    [TestMethod]
    public async Task UnlinkedOrderFilterRunsBeforeCountAndPaging()
    {
        await using var db = new TrackingFixture();
        await db.InitializeAsync();
        var ids = new List<Guid>();
        for (var i = 0; i < 6; i++) ids.Add((await db.CreateAsync()).Data!.PurchaseOrderId);
        await db.ExecuteAsync("""
            UPDATE public.purchase_order_head SET confirmation_status=1 WHERE uid=@partial;
            UPDATE public.purchase_order_line SET unlinked_qty=4 WHERE purchase_order_id=@partial;
            UPDATE public.purchase_order_line SET unlinked_qty=0 WHERE purchase_order_id=@zero;
            UPDATE public.purchase_order_line SET unlinked_qty=NULL WHERE purchase_order_id=@unknown;
            UPDATE public.purchase_order_line SET is_deleted=TRUE WHERE purchase_order_id=@removed;
            UPDATE public.purchase_order_head SET lifecycle_status=@voidedStatus WHERE uid=@voided;
            """, new { partial = ids[1], zero = ids[2], unknown = ids[3], removed = ids[4], voided = ids[5], voidedStatus = (int)PurchaseOrderLifecycleStatusEnum.Voided });
        var selected = new List<Guid>();
        for (var page = 1; page <= 2; page++)
        {
            var result = await db.InScopeAsync(p => p.GetRequiredService<IPurchaseOrderManagerQueryService>()
                .SearchAsync(new() { CustomerCode = db.CustomerCode, OnlyWithUnlinkedQuantity = true, PageIndex = page, PageSize = 1 }));
            Assert.IsTrue(result.Success, result.Message);
            Assert.AreEqual(2, result.Data!.TotalCount);
            selected.Add(result.Data.Items.Single().PurchaseOrderId);
        }
        CollectionAssert.AreEquivalent(ids.Take(2).ToList(), selected);
        var normal = await db.InScopeAsync(p => p.GetRequiredService<IPurchaseOrderManagerQueryService>().SearchAsync(new() { CustomerCode = db.CustomerCode }));
        Assert.IsTrue(normal.Success, normal.Message);
        Assert.AreEqual(6, normal.Data!.TotalCount);
    }

    /// <summary>未反馈、明确零与部分货好分开筛选；空明细不会被误判为全部完成。</summary>
    [TestMethod]
    public async Task ReadyAndReceiptStatesFilterBeforePaging()
    {
        await using var db = new TrackingFixture();
        await db.InitializeAsync();
        var first = (await db.CreateAsync()).Data!;
        var second = (await db.CreateAsync()).Data!;
        var third = (await db.CreateAsync()).Data!;
        var empty = (await db.CreateAsync()).Data!;
        var secondLine = await db.ScalarAsync<Guid>("SELECT uid FROM public.purchase_order_line WHERE purchase_order_id=@id LIMIT 1", new { id = second.PurchaseOrderId });
        var thirdLine = await db.ScalarAsync<Guid>("SELECT uid FROM public.purchase_order_line WHERE purchase_order_id=@id LIMIT 1", new { id = third.PurchaseOrderId });
        await db.ExecuteAsync("""
            INSERT INTO public.supplier_progress_report(uid,purchase_order_line_id,sequence,arrangement_status,ready_qty,supplier_reported_date,command_id,is_correction)
            VALUES(gen_random_uuid(),@secondLine,1,0,0,timezone('UTC',clock_timestamp()),gen_random_uuid(),FALSE),
                  (gen_random_uuid(),@thirdLine,1,0,4,timezone('UTC',clock_timestamp()),gen_random_uuid(),FALSE);
            UPDATE public.purchase_order_line SET is_deleted=TRUE WHERE purchase_order_id=@emptyId;
            """, new { secondLine, thirdLine, emptyId = empty.PurchaseOrderId });

        async Task<PurchaseOrderManagerListItemDto> Find(PurchaseReadyStatusEnum status)
        {
            var result = await db.InScopeAsync(p => p.GetRequiredService<IPurchaseOrderManagerQueryService>()
                .SearchAsync(new PurchaseOrderManagerSearchRequest { CustomerCode = db.CustomerCode, ReadyStatuses = [status], PageIndex = 1, PageSize = 1 }));
            Assert.IsTrue(result.Success, result.Message);
            Assert.AreEqual(1, result.Data!.TotalCount);
            return result.Data.Items.Single();
        }
        Assert.AreEqual(first.PurchaseOrderId, (await Find(PurchaseReadyStatusEnum.NotReported)).PurchaseOrderId);
        Assert.AreEqual(second.PurchaseOrderId, (await Find(PurchaseReadyStatusEnum.NotReady)).PurchaseOrderId);
        Assert.AreEqual(third.PurchaseOrderId, (await Find(PurchaseReadyStatusEnum.PartiallyReady)).PurchaseOrderId);
        Assert.AreEqual(empty.PurchaseOrderId, (await Find(PurchaseReadyStatusEnum.NoLines)).PurchaseOrderId);

        await db.ExecuteAsync("""
            INSERT INTO public.inbound_document(uid,document_no,document_type,creation_mode,lifecycle_status,posting_status,
                warehouse_site_id,receipt_date)
            VALUES(@inboundId,@DocumentNo,10,0,0,1,@site,timezone('UTC',clock_timestamp()));
            INSERT INTO public.inbound_document_line(uid,inbound_document_id,line_number,product_id,sku_snapshot,unit_snapshot,quantity,source_line_id)
            VALUES(gen_random_uuid(),@inboundId,1,@product,'TEST-FG','PCS',3,@thirdLine);
            """, new { inboundId = Guid.NewGuid(), site = db.ReceiptSiteId, product = db.ProductId, thirdLine, DocumentNo = db.Batch + "_TEST-RECEIPT-TRACK" });

        var receipt = await db.InScopeAsync(p => p.GetRequiredService<IPurchaseOrderManagerQueryService>()
            .SearchAsync(new PurchaseOrderManagerSearchRequest { CustomerCode = db.CustomerCode, ReceiptStatuses = [PurchaseReceiptStatusEnum.NotReceived], PageSize = 2 }));
        Assert.IsTrue(receipt.Success, receipt.Message);
        Assert.AreEqual(2, receipt.Data!.TotalCount);
        Assert.AreEqual(2, receipt.Data.Items.Count);
        var partialReceipt = await db.InScopeAsync(p => p.GetRequiredService<IPurchaseOrderManagerQueryService>()
            .SearchAsync(new PurchaseOrderManagerSearchRequest { CustomerCode = db.CustomerCode, ReceiptStatuses = [PurchaseReceiptStatusEnum.PartiallyReceived] }));
        Assert.IsTrue(partialReceipt.Success, partialReceipt.Message);
        Assert.AreEqual(third.PurchaseOrderId, partialReceipt.Data!.Items.Single().PurchaseOrderId);
    }

    /// <summary>独立采购无需求 Link 时仍返回采购行；混合单位不显示跨行数量合计。</summary>
    [TestMethod]
    public async Task IndependentTraceAndMixedUnitsRemainExplicit()
    {
        await using var db = new TrackingFixture();
        await db.InitializeAsync();
        var command = db.CreateCommand();
        command.Lines.Add(new() { ProductId = db.ComponentId, Quantity = 5m, UnitPrice = 2m, TaxPrice = 2.5m,
            DeliverySupplierId = db.SupplierId });
        var created = await db.CreateAsync(command);
        Assert.IsTrue(created.Success, created.Message);
        var uid = created.Data!.PurchaseOrderId;
        await db.ExecuteAsync("UPDATE public.purchase_order_line SET unit_snapshot='BOX' WHERE purchase_order_id=@uid AND line_number=2", new { uid });
        var list = await db.InScopeAsync(p => p.GetRequiredService<IPurchaseOrderManagerQueryService>()
            .SearchAsync(new PurchaseOrderManagerSearchRequest { Keyword = created.Data.OrderNo }));
        Assert.IsTrue(list.Success, list.Message);
        var row = list.Data!.Items.Single();
        Assert.AreEqual(2, row.LineCount);
        Assert.IsNull(row.TotalQty);
        Assert.IsNull(row.ReceiptProgress);
        var trace = await db.InScopeAsync(p => p.GetRequiredService<IPurchaseOrderTraceService>().GetAsync(uid));
        Assert.IsTrue(trace.Success, trace.Message);
        Assert.AreEqual(2, trace.Data!.OriginKind);
        Assert.AreEqual(2, trace.Data.Lines.Count);
        Assert.IsTrue(trace.Data.Lines.All(line => line.Links.Count == 0));
        await db.ExecuteAsync("""
            INSERT INTO public.inbound_document(uid,document_no,document_type,creation_mode,lifecycle_status,posting_status,warehouse_site_id,receipt_date)
            VALUES(gen_random_uuid(),@DocumentNo,10,0,0,8,@site,timezone('UTC',clock_timestamp()));
            """, new { site = db.ReceiptSiteId, DocumentNo = db.Batch + "_IR-TRACE-UNVERIFIED" });
        var unrelatedReceipt = await db.InScopeAsync(p => p.GetRequiredService<IPurchaseOrderTraceService>().GetAsync(uid));
        Assert.IsTrue(unrelatedReceipt.Success, unrelatedReceipt.Message);
        var purchaseLineId = await db.ScalarAsync<Guid>(
            "SELECT uid FROM public.purchase_order_line WHERE purchase_order_id=@uid ORDER BY line_number LIMIT 1", new { uid });
        await db.ExecuteAsync("""
            INSERT INTO public.inbound_document(uid,document_no,document_type,creation_mode,lifecycle_status,posting_status,warehouse_site_id,receipt_date)
            VALUES(@receipt,@DocumentNo,10,0,0,8,@site,timezone('UTC',clock_timestamp()));
            INSERT INTO public.inbound_document_line(uid,inbound_document_id,line_number,product_id,sku_snapshot,unit_snapshot,quantity,source_line_id)
            VALUES(gen_random_uuid(),@receipt,1,@productId,'TEST-FG','PCS',1,@purchaseLineId);
            """, new { receipt = Guid.NewGuid(), site = db.ReceiptSiteId, productId = db.ProductId, purchaseLineId, DocumentNo = db.Batch + "_IR-TRACE-RELATED" });
        var unknownReceipt = await db.InScopeAsync(p => p.GetRequiredService<IPurchaseOrderTraceService>().GetAsync(uid));
        Assert.IsFalse(unknownReceipt.Success);
        StringAssert.Contains(unknownReceipt.Message, "收货量未知");
    }

    /// <summary>北京时间边界以采购行计划交期判定；未知货好量作为风险命中，已货好行退出提醒。</summary>
    [TestMethod]
    public async Task DueViewsUseBeijingDateAndUnconfirmedReadyRisk()
    {
        await using var db = new TrackingFixture();
        await db.InitializeAsync();
        var late = (await db.CreateAsync()).Data!;
        var soon = (await db.CreateAsync()).Data!;
        var later = (await db.CreateAsync()).Data!;
        var today = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTimeOffset.UtcNow, "China Standard Time").Date;
        await db.ExecuteAsync("""
            UPDATE public.purchase_order_head SET confirmation_status=1 WHERE uid=ANY(@ids);
            UPDATE public.purchase_order_line SET planned_delivery_date=@lateDate WHERE purchase_order_id=@lateId;
            UPDATE public.purchase_order_line SET planned_delivery_date=@soonDate WHERE purchase_order_id=@soonId;
            UPDATE public.purchase_order_line SET planned_delivery_date=@laterDate WHERE purchase_order_id=@laterId;
            """, new { ids = new[] { late.PurchaseOrderId, soon.PurchaseOrderId, later.PurchaseOrderId },
                lateId = late.PurchaseOrderId, soonId = soon.PurchaseOrderId, laterId = later.PurchaseOrderId,
                lateDate = today.AddDays(-1), soonDate = today.AddDays(7), laterDate = today.AddDays(8) });

        async Task<PurchaseOrderManagerListItemDto> Find(PurchaseDueViewEnum view)
        {
            var result = await db.InScopeAsync(p => p.GetRequiredService<IPurchaseOrderManagerQueryService>()
                .SearchAsync(new PurchaseOrderManagerSearchRequest { CustomerCode = db.CustomerCode, DueView = view }));
            Assert.IsTrue(result.Success, result.Message);
            Assert.AreEqual(1, result.Data!.TotalCount);
            return result.Data.Items.Single();
        }
        var overdue = await Find(PurchaseDueViewEnum.OverdueNotReady);
        Assert.AreEqual(late.PurchaseOrderId, overdue.PurchaseOrderId);
        Assert.IsTrue(overdue.HasUnconfirmedReadyRisk);
        Assert.AreEqual(soon.PurchaseOrderId, (await Find(PurchaseDueViewEnum.DueSoonNotReady)).PurchaseOrderId);

        var soonLine = await db.ScalarAsync<Guid>("SELECT uid FROM public.purchase_order_line WHERE purchase_order_id=@id LIMIT 1", new { id = soon.PurchaseOrderId });
        await db.ExecuteAsync("""
            INSERT INTO public.supplier_progress_report(uid,purchase_order_line_id,sequence,arrangement_status,ready_qty,supplier_reported_date,command_id,is_correction)
            VALUES(gen_random_uuid(),@soonLine,1,0,10,timezone('UTC',clock_timestamp()),gen_random_uuid(),FALSE);
            """, new { soonLine });
        var noSoon = await db.InScopeAsync(p => p.GetRequiredService<IPurchaseOrderManagerQueryService>()
            .SearchAsync(new PurchaseOrderManagerSearchRequest { CustomerCode = db.CustomerCode, DueView = PurchaseDueViewEnum.DueSoonNotReady }));
        Assert.IsTrue(noSoon.Success, noSoon.Message);
        Assert.AreEqual(0, noSoon.Data!.TotalCount);
    }

    private static readonly Dictionary<string, (Guid[] Ids, string Snapshot)> OriginalRows = [];
    private static long OriginalSequence;
    private static string OriginalStructure = "";
    private static string OriginalSemanticStructure = "";
    private static readonly List<object> OwnedDatabases = [];

    /// <summary>冻结原50表身份及完整摘要；其他活动事务存在时停止，不初始化或复位。</summary>
    [ClassInitialize]
    public static async Task CaptureOriginalRowsAsync(TestContext context)
    {
        if (Environment.GetEnvironmentVariable("SWIFTEXPORT_RUN_PG_TRACKING_ISOLATED_TESTS") != "1")
            Assert.Inconclusive("本批四个独立测试库须显式启用；不自动建库。");
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
        OriginalStructure = await StructureSnapshotAsync(session);
        OriginalSemanticStructure = await StructureSnapshotAsync(session, includeColumnComments: false);
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
        var structure = await StructureSnapshotAsync(session);
        Directory.CreateDirectory("TestResults/postgresql-migration");
        await File.WriteAllTextAsync("TestResults/postgresql-migration/p09-s02e-purchase-tracking-isolated-data-protection.json", JsonSerializer.Serialize(new
        {
            RecordedAt = DateTimeOffset.Now, Database = "dv_next_pg_failure_tests_20261004",
            SequenceBefore = OriginalSequence, SequenceAfter = sequence, Tables = evidence,
            StructureBefore = OriginalStructure, StructureAfter = structure, SemanticStructure = OriginalSemanticStructure, OwnedDatabases
        }, new JsonSerializerOptions { WriteIndented = true }));
        Assert.IsTrue(unchanged, "原记录被修改或删除；逐表证据已保存。");
        Assert.AreEqual(OriginalSequence, sequence, "采购查询跟踪不能产生库存事实。");
        Assert.AreEqual(OriginalStructure, structure, "源failure库结构不能改变。");
        Assert.AreEqual(4, OwnedDatabases.Count, "四个原方法各自留存独立库事实。");
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


    /// <summary>全摘要保护各库定义及注释；跨库只排除已留证的列注释差异，OID/所有者/序列当前值始终排除。</summary>
    private static async Task<string> StructureSnapshotAsync(DbSession session, bool includeColumnComments = true)
        => await session.Connection.ExecuteScalarAsync<string>("""
            SELECT md5(COALESCE(string_agg(payload,'' ORDER BY payload),'')) FROM (
                SELECT jsonb_build_array('column',table_name,column_name,ordinal_position,data_type,udt_name,is_nullable,
                    column_default,character_maximum_length,numeric_precision,numeric_scale,datetime_precision)::text AS payload
                FROM information_schema.columns WHERE table_schema='public'
                UNION ALL
                SELECT jsonb_build_array('constraint',r.relname,c.conname,pg_get_constraintdef(c.oid,true))::text
                FROM pg_constraint c JOIN pg_class r ON r.oid=c.conrelid JOIN pg_namespace n ON n.oid=r.relnamespace
                WHERE n.nspname='public'
                UNION ALL
                SELECT jsonb_build_array('index',tablename,indexname,indexdef)::text FROM pg_indexes WHERE schemaname='public'
                UNION ALL
                SELECT jsonb_build_array('function',p.proname,pg_get_functiondef(p.oid))::text
                FROM pg_proc p JOIN pg_namespace n ON n.oid=p.pronamespace WHERE n.nspname='public' AND p.prokind='f'
                UNION ALL
                SELECT jsonb_build_array('trigger',r.relname,t.tgname,pg_get_triggerdef(t.oid,true))::text
                FROM pg_trigger t JOIN pg_class r ON r.oid=t.tgrelid JOIN pg_namespace n ON n.oid=r.relnamespace
                WHERE n.nspname='public' AND NOT t.tgisinternal
                UNION ALL
                SELECT jsonb_build_array('collation',to_jsonb(c)-'oid'-'collowner'-'collnamespace'-'collversion')::text
                FROM pg_collation c JOIN pg_namespace n ON n.oid=c.collnamespace WHERE n.nspname='public'
                UNION ALL
                SELECT jsonb_build_array('sequence',sequencename,data_type,start_value,min_value,max_value,increment_by,cycle,cache_size)::text
                FROM pg_sequences WHERE schemaname='public'
                UNION ALL
                SELECT jsonb_build_array('table_comment',r.relname,obj_description(r.oid,'pg_class'))::text
                FROM pg_class r JOIN pg_namespace n ON n.oid=r.relnamespace WHERE n.nspname='public' AND r.relkind='r'
                UNION ALL
                SELECT jsonb_build_array('column_comment',r.relname,a.attname,col_description(r.oid,a.attnum))::text
                FROM pg_attribute a JOIN pg_class r ON r.oid=a.attrelid JOIN pg_namespace n ON n.oid=r.relnamespace
                WHERE n.nspname='public' AND r.relkind='r' AND a.attnum>0 AND NOT a.attisdropped
            ) definitions WHERE @includeColumnComments OR payload::jsonb->>0<>'column_comment'
            """, new { includeColumnComments }) ?? throw new AssertFailedException("结构摘要为空。");

    /// <summary>只供原四跟踪方法，每方法一个已授权新PG库；正常创建采购并保留全部事实，不清理任何库。</summary>
    private sealed class TrackingFixture : IAsyncDisposable
    {
        private readonly string sourceConnection = PostgreSqlIntegrationEnvironment.GetFailureConnectionString();
        private readonly string connection;
        private readonly string databaseName;
        private readonly Dictionary<string, (Guid[] Ids, string Snapshot)> copiedRows = [];
        private bool databaseCreated;
        private string ownedStructure = "";
        private readonly int orderYear = 4000 + Math.Abs(Guid.NewGuid().GetHashCode() % 5000);
        private WebApplication? app;
        public string Batch { get; } = "PGS02E_" + Guid.NewGuid().ToString("N");
        public string CustomerCode => Batch + "_C";
        public Guid CustomerId { get; } = Guid.NewGuid();
        public Guid SupplierId { get; } = Guid.NewGuid();
        public Guid ReceiptSupplierId { get; } = Guid.NewGuid();
        public Guid ProductId { get; } = Guid.NewGuid();
        public Guid ComponentId { get; } = Guid.NewGuid();
        public Guid ReceiptSiteId { get; } = Guid.NewGuid();


        /// <summary>调用成员只能是获准的四个原方法；库名固定，不接受外部库名或已有库复位。</summary>
        public TrackingFixture([CallerMemberName] string method = "")
        {
            databaseName = method switch
            {
                nameof(UnlinkedOrderFilterRunsBeforeCountAndPaging) => "dv_next_pg_tracking_tests_20261006_a2f4bf37b4ca",
                nameof(ReadyAndReceiptStatesFilterBeforePaging) => "dv_next_pg_tracking_tests_20261006_e1d7f55c9da9",
                nameof(IndependentTraceAndMixedUnitsRemainExplicit) => "dv_next_pg_tracking_tests_20261006_4bf8ba322373",
                nameof(DueViewsUseBeijingDateAndUnconfirmedReadyRisk) => "dv_next_pg_tracking_tests_20261006_75921daa825c",
                _ => throw new InvalidOperationException("拒绝未获准方法创建测试库。")
            };
            connection = new NpgsqlConnectionStringBuilder(sourceConnection)
            {
                Database = databaseName, Pooling = false, ApplicationName = "PG-S02E-ISOLATED"
            }.ConnectionString;
        }

        /// <summary>只初始化列明新库或接续本批固定SHA留证的空新库；来源九表只读复制，不重建、覆盖或清理。</summary>
        private async Task PrepareOwnedDatabaseAsync()
        {
            var baseline = await File.ReadAllBytesAsync(Path.Combine(Root(), "SwiftExport.Infrastructure/Database/Generated/SwiftExport.PostgreSql.Schema.preview.sql"));
            Assert.AreEqual("FE5481C997822F653965406420B60B0A0857BD60570927976520DD1A778959F7",
                Convert.ToHexString(SHA256.HashData(baseline)), "固定已批准结构不能变化。");
            using var source = new DbSession(new PostgreSqlConnectionFactory(sourceConnection));
            Assert.IsTrue(await source.Connection.ExecuteScalarAsync<bool>(
                "SELECT rolcreatedb OR rolsuper FROM pg_roles WHERE rolname=current_user"), "实际连接角色必须具有建库权限。");
            var exists = await source.Connection.ExecuteScalarAsync<int>(
                "SELECT count(*) FROM pg_database WHERE datname=@databaseName", new { databaseName });
            using var target = new DbSession(new PostgreSqlConnectionFactory(connection));
            if (exists == 0)
            {
                await source.Connection.ExecuteAsync($"CREATE DATABASE {PostgreSqlNames.Quote(databaseName)}");
                databaseCreated = true;
                await target.Connection.ExecuteAsync(System.Text.Encoding.UTF8.GetString(baseline), commandTimeout: 120);
            }
            else
            {
                // 只接续本批已留证的四个空新库，不接受其他已有库，不DDL、清理或重复写成功事实。
                var checkpoint = await File.ReadAllBytesAsync(Path.Combine(Root(),
                    "TestResults/postgresql-migration/p09-s02e-purchase-tracking-isolated-first-data-protection.json"));
                Assert.AreEqual("7D282567568F9F113E6ECD7E6DEDE45572C06E51D4A6E09C46545337AC99D632",
                    Convert.ToHexString(SHA256.HashData(checkpoint)), "首次空新库事实必须原样留存。");
                using var proof = JsonDocument.Parse(checkpoint);
                var owned = proof.RootElement.GetProperty("OwnedDatabases").EnumerateArray()
                    .Single(x => x.GetProperty("Database").GetString() == databaseName);
                Assert.IsFalse(owned.GetProperty("BaselineCopyComplete").GetBoolean(), "已完成初始化的库不能重放。");
                Assert.AreEqual(0, owned.GetProperty("Tables").GetArrayLength(), "只能接续尚未复制基线的空库。");
                Assert.AreEqual(owned.GetProperty("StructureAfter").GetString(), await StructureSnapshotAsync(target), "空新库结构必须与首轮证据一致。");
                var emptyTables = (await target.Connection.QueryAsync<string>(
                    "SELECT table_name FROM information_schema.tables WHERE table_schema='public' AND table_type='BASE TABLE'")).ToArray();
                Assert.AreEqual(50, emptyTables.Length);
                foreach (var table in emptyTables)
                    Assert.AreEqual(0L, await target.Connection.ExecuteScalarAsync<long>(
                        $"SELECT count(*) FROM public.{PostgreSqlNames.Quote(table)}"), "已产生数据的新库不能重放。");
                databaseCreated = true;
            }
            ownedStructure = await StructureSnapshotAsync(target);
            Assert.AreEqual(OriginalSemanticStructure, await StructureSnapshotAsync(target, includeColumnComments: false),
                "表列/约束/索引/函数/触发器/排序/序列必须对应现有PG，列注释差异单独保护。");

            var actorId = await source.Connection.QuerySingleAsync<Guid>(
                "SELECT uid FROM public.sys_user WHERE user_name='dv' AND is_active AND NOT is_deleted");
            var roleId = await source.Connection.QuerySingleAsync<Guid>(
                "SELECT uid FROM public.sys_role WHERE role_code='SYSTEM_ADMIN' AND is_active AND NOT is_deleted");
            foreach (var (table, where) in new (string Table, string Where)[]
            {
                ("sys_dict_type", ""), ("sys_dict_data", ""), ("sys_dic_currency", ""),
                ("sys_dic_currency_exchange", ""), ("sys_permission", ""),
                ("sys_role", "WHERE uid=@roleId"), ("sys_user", "WHERE uid=@actorId"),
                ("sys_role_permission", "WHERE role_id=@roleId"),
                ("sys_user_role", "WHERE user_id=@actorId AND role_id=@roleId")
            })
            {
                // 认证字段仅作为内存JSON参数复制到获准新库，不输出行、密码或哈希。
                var rows = await source.Connection.ExecuteScalarAsync<string>(
                    $"SELECT COALESCE(jsonb_agg(to_jsonb(t)),'[]'::jsonb)::text FROM public.{PostgreSqlNames.Quote(table)} t {where}",
                    new { actorId, roleId });
                await target.Connection.ExecuteAsync(
                    $"INSERT INTO public.{PostgreSqlNames.Quote(table)} SELECT * FROM jsonb_populate_recordset(NULL::public.{PostgreSqlNames.Quote(table)},CAST(@rows AS jsonb))",
                    new { rows });
            }
            var tables = (await target.Connection.QueryAsync<string>(
                "SELECT table_name FROM information_schema.tables WHERE table_schema='public' AND table_type='BASE TABLE' ORDER BY table_name")).ToArray();
            Assert.AreEqual(50, tables.Length, "只使用获准的现50表结构。");
            foreach (var table in tables)
            {
                var ids = (await target.Connection.QueryAsync<Guid>($"SELECT uid FROM public.{PostgreSqlNames.Quote(table)}")).ToArray();
                copiedRows.Add(table, (ids, await SnapshotAsync(target, table, ids)));
            }
        }

        /// <summary>准备独立新库后读取复制基线的dv和PCS，沿用原随机主数据及未使用年月，保留正常创建服务。</summary>
        public async Task InitializeAsync()
        {
            await PrepareOwnedDatabaseAsync();
            using var session = new DbSession(new PostgreSqlConnectionFactory(connection));
            var actor = await session.Connection.QuerySingleAsync<TrackingActor>("""
                SELECT u.uid AS "UserId",u.user_name AS "UserName" FROM public.sys_user u
                WHERE u.user_name='dv' AND u.is_active AND NOT u.is_deleted AND EXISTS(
                    SELECT 1 FROM public.sys_user_role ur JOIN public.sys_role r ON r.uid=ur.role_id
                    WHERE ur.user_id=u.uid AND NOT ur.is_deleted AND r.is_active AND NOT r.is_deleted AND r.role_code='SYSTEM_ADMIN');
                """);
            Assert.AreEqual(1, await session.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.sys_dict_data WHERE dict_code='Unit' AND data_name_en='PCS' AND is_enabled AND NOT is_deleted"), "只读取现有PCS，不增改字典。");
            Assert.AreEqual(0, await session.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.document_sequence WHERE doc_type='PO' AND year_month=@Month", new { Month = $"{orderYear:D4}08" }), "本批年月必须原不存在，不重置已有号段。");
            await session.Connection.ExecuteAsync("""
                INSERT INTO public.customer(uid,customer_code,customer_name,doc_relate_no,deposit_rate,lead_time_days,is_active)
                VALUES(@CustomerId,@CustomerCode,'原采购跟踪客户',@CustomerCode,0,0,TRUE);
                INSERT INTO public.supplier(uid,supplier_code,supplier_name,doc_relate_no,is_active)
                VALUES(@SupplierId,@ActualCode,'实际供应商',@ActualCode,TRUE),
                      (@ReceiptSupplierId,@ReceiptCode,'收货供应商',@ReceiptCode,TRUE);
                INSERT INTO public.product(uid,sku,description_zh,unit,material_type,supply_type,bom_version,color_id,is_active)
                VALUES(@ProductId,@RootSku,'采购成品','PCS','成品','采购',0,@EmptyId,TRUE),
                      (@ComponentId,@ComponentSku,'客供件','PCS','客供件','采购',0,@EmptyId,TRUE);
                INSERT INTO public.warehouse_site(uid,code,name,site_type,supplier_id,is_active)
                VALUES(@ReceiptSiteId,@SiteCode,'独立新收货仓',0,@ReceiptSupplierId,TRUE);
                """, new { CustomerId, CustomerCode, SupplierId, ReceiptSupplierId, ProductId, ComponentId, ReceiptSiteId,
                    ActualCode = Batch + "_ACTUAL", ReceiptCode = Batch + "_RECEIPT", RootSku = Batch + "_FG",
                    ComponentSku = Batch + "_B", SiteCode = Batch + "_R", EmptyId = Guid.Empty });
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = Path.Combine(Root(), "SwiftExport.WebApi"), EnvironmentName = "Development" });
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Default"] = connection });
            builder.AddWebApiServices(); builder.Services.AddSingleton<ICurrentUser>(actor);
            app = builder.Build();
        }

        /// <summary>保留原创建数量、价格、包装及日期月底，只将年月移入原不存在的独立号段。</summary>
        public CreateIndependentPurchaseOrderCommand CreateCommand() => new()
        {
            CommandId = Guid.NewGuid(), CustomerId = CustomerId, SupplierId = SupplierId,
            OrderDate = new DateTime(orderYear, 8, 31), PackagingRequirements = "客户包装要求",
            Lines = [new() { ProductId = ProductId, Quantity = 10m, UnitPrice = 2m, TaxPrice = 2.5m }]
        };

        /// <summary>使用原真实采购创建服务及权限，成功事实/审计/回执留存，不伪造创建结果。</summary>
        public Task<SwiftExport.Contracts.Wrappers.ServiceResult<ProcurementOrderCommandResult>> CreateAsync(CreateIndependentPurchaseOrderCommand? command = null)
            => InScopeAsync(p => p.GetRequiredService<IProcurementOrderDraftService>().CreateAsync(command ?? CreateCommand()));

        /// <summary>执行本文件明确的PG夹具SQL，调用方只传本批新UID，不在运行时翻译SQL。</summary>
        public async Task<int> ExecuteAsync(string sql, object? args = null)
        {
            using var session = new DbSession(new PostgreSqlConnectionFactory(connection));
            return await session.Connection.ExecuteAsync(sql, args);
        }

        /// <summary>通过真实独立连接读取本批夹具身份，不修改数据库。</summary>
        public async Task<T> ScalarAsync<T>(string sql, object? args = null)
        {
            using var session = new DbSession(new PostgreSqlConnectionFactory(connection));
            return (await session.Connection.ExecuteScalarAsync<T>(sql, args))!;
        }

        /// <summary>原服务各自拥有独立request scope及事务，读取/命令结束后检查清理，不借测试事务。</summary>
        public async Task<T> InScopeAsync<T>(Func<IServiceProvider, Task<T>> action)
        {
            using var scope = app!.Services.CreateScope();
            var result = await action(scope.ServiceProvider);
            var session = scope.ServiceProvider.GetRequiredService<DbSession>();
            Assert.IsNull(session.Transaction); Assert.IsNull(session.CommandLease); Assert.IsNull(session.SourceDemandLease);
            return result;
        }


        /// <summary>只关闭服务，核对复制基线和结构；保留新库及所有事实，不删除或清理。</summary>
        public async ValueTask DisposeAsync()
        {
            if (app is not null) await app.DisposeAsync();
            if (!databaseCreated) return;
            using var target = new DbSession(new PostgreSqlConnectionFactory(connection));
            var tables = new List<object>(); var unchanged = true;
            foreach (var (table, original) in copiedRows)
            {
                var after = await SnapshotAsync(target, table, original.Ids);
                var parameters = new PostgreSqlParameters(); parameters.AddIds("OldIds", original.Ids);
                var added = (await target.Connection.QueryAsync<Guid>(
                    $"SELECT uid FROM public.{PostgreSqlNames.Quote(table)} WHERE NOT(uid=ANY(@OldIds)) ORDER BY uid", parameters)).ToArray();
                tables.Add(new { Table = table, OldCount = original.Ids.Length, Before = original.Snapshot, After = after, AddedIds = added });
                unchanged &= original.Snapshot == after;
            }
            var structure = await StructureSnapshotAsync(target);
            var sequence = await target.Connection.QuerySingleAsync<string>(
                "SELECT last_value::text||':'||is_called::text FROM public.inventory_ledger_sequence");
            OwnedDatabases.Add(new { Database = databaseName, Tables = tables, StructureBefore = ownedStructure,
                StructureAfter = structure, InventorySequence = sequence, BaselineCopyComplete = copiedRows.Count == 50 });
            // 初始化已抛错时保留原异常，不能用清理期第二异常覆盖诊断；不完整状态仍写入证据。
            if (copiedRows.Count != 50) return;
            Assert.AreEqual(50, copiedRows.Count, "新库基线复制和保护必须完成。");
            Assert.IsTrue(unchanged, "新库复制的基线行不得修改。");
            Assert.AreEqual(ownedStructure, structure, "新库结构和注释不得随场景变化。");
            Assert.AreEqual("1:false", sequence, "采购跟踪不推进库存序列。");
        }

    }

    /// <summary>读取实际dv资格后使用原权限服务，不创建或改变账号，也不声称本批验证HTTP/JWT登录。</summary>
    private sealed class TrackingActor : ICurrentUser
    {
        public Guid? UserId { get; set; }
        public string UserName { get; set; } = "";
        /// <summary>资格来自本库已核实有效管理员，服务仍执行其原业务权限。</summary>
        public bool IsAuthenticated => true;
    }

    /// <summary>定位宿主配置，只在内存覆盖授权测试连接，不改配置或启动HTTP服务器。</summary>
    private static string Root()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "SwiftExport.WebApi", "appsettings.json"))) return directory.FullName;
        throw new DirectoryNotFoundException("无法定位 WebApi 配置。");
    }
}
