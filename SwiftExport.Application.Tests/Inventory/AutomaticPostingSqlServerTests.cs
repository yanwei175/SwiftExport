using SwiftExport.Application.Services.Systems.Numbering;
using SwiftExport.Infrastructure.Repositories.Systems.Numbering;
using SwiftExport.Infrastructure.Repositories.Inventory.Inbounds;
using SwiftExport.Infrastructure.Repositories.Inventory.Outbounds;
using SwiftExport.Application.Interfaces.Inventory.AutomaticDocuments;
using SwiftExport.Application.Services.Inventory.Posting;
using SwiftExport.Application.Services.Inventory.AutomaticDocuments;
using System.Data;
using Dapper;
using Npgsql;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.Inventory;
using SwiftExport.Application.Services.Inventory;
using SwiftExport.Application.Services.Systems;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities.Inventory;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Entities.Procurement;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Application.Tests.Database;
using SwiftExport.Infrastructure.Repositories.Auditing;
using SwiftExport.Infrastructure.Repositories.Base;
using SwiftExport.Infrastructure.Repositories.Inventory;
using SwiftExport.Infrastructure.Repositories.Inventory.Posting;
using SwiftExport.Infrastructure.Repositories.Inventory.AutomaticDocuments;
using SwiftExport.Infrastructure.Repositories.Inventory.Warehouses;
using SwiftExport.Infrastructure.Repositories.Systems;
using SwiftExport.Infrastructure.Services.Auth;
using SwiftExport.Application.Interfaces.Auth;
using SwiftExport.Application.Interfaces.Auth.Permissions.Evaluation;
using SwiftExport.Application.Interfaces.Shipping;
using SwiftExport.Application.Services.Shipping;
using SwiftExport.Application.Services.Shipping.PackingLists;
using SwiftExport.Core.Entities.DemandPlanning;
using SwiftExport.Core.Entities.Shipping;
using SwiftExport.Core.Interfaces.Shipping;
using SwiftExport.Infrastructure.Repositories.Shipping;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Contracts.SysEnums.Shipping;
using Proxy = SwiftExport.Application.Tests.Shipping.PackingListPostingCoordinatorTests.Proxy;

namespace SwiftExport.Application.Tests.Inventory;

/// <summary>沿用六个自动过账行为场景，显式连接独立 PG 库，保护全部原记录，不读取应用默认连接或部署结构。</summary>
[TestClass]
[DoNotParallelize]
[TestCategory("PostgreSqlIntegration")]
public sealed class AutomaticPostingPostgreSqlTests
{
    private static readonly Dictionary<string, (Guid[] Ids, string Snapshot)> OriginalRows = [];
    private static DateTime BusinessDate;
    private static long OriginalSequence;

    /// <summary>首次写入前读取现有50表原身份和完整摘要，选择未占用测试月份；不初始化、清理或覆盖任何数据。</summary>
    [ClassInitialize]
    public static async Task CaptureOriginalRowsAsync(TestContext context)
    {
        using var session = new DbSession(new PostgreSqlConnectionFactory(ConnectionString()));
        // 空闲观察连接不参与写入；拒绝其他活动或未结束事务，不擅自结束连接。
        Assert.AreEqual(0, await session.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM pg_stat_activity WHERE datname=current_database() AND pid<>pg_backend_pid() AND backend_type='client backend' AND (state<>'idle' OR xact_start IS NOT NULL)"), "隔离写入必须串行且没有其他活动事务。");
        var tables = (await session.Connection.QueryAsync<string>("SELECT table_name FROM information_schema.tables WHERE table_schema='public' AND table_type='BASE TABLE' ORDER BY table_name")).ToArray();
        Assert.AreEqual(50, tables.Length, "复用现有结构，不初始化测试库。");
        foreach (var table in tables)
        {
            var ids = (await session.Connection.QueryAsync<Guid>($"SELECT uid FROM public.{PostgreSqlNames.Quote(table)}")).ToArray();
            OriginalRows.Add(table, (ids, await SnapshotAsync(session, table, ids)));
        }
        OriginalSequence = await session.Connection.ExecuteScalarAsync<long>("SELECT last_value FROM public.inventory_ledger_sequence");
        for (var attempt = 0; attempt < 24; attempt++)
        {
            BusinessDate = new DateTime(8000 + Random.Shared.Next(1000), Random.Shared.Next(1, 13), 1);
            if (await session.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.document_sequence WHERE year_month=@Month", new { Month = BusinessDate.ToString("yyyyMM") }) == 0)
                return;
        }
        Assert.Fail("没有找到未占用测试月份，禁止修改既有号段。");
    }

    /// <summary>六场景结束后验证所有旧身份完整行及数量不变，保存每表摘要、新身份和序列前后值；成功事实保留，不清库。</summary>
    [ClassCleanup]
    public static async Task VerifyOriginalRowsAsync()
    {
        if (OriginalRows.Count == 0) return;
        using var session = new DbSession(new PostgreSqlConnectionFactory(ConnectionString()));
        var evidence = new List<object>();
        foreach (var (table, original) in OriginalRows)
        {
            var after = await SnapshotAsync(session, table, original.Ids);
            var parameters = new PostgreSqlParameters(); parameters.AddIds("OldIds", original.Ids);
            var added = (await session.Connection.QueryAsync<Guid>($"SELECT uid FROM public.{PostgreSqlNames.Quote(table)} WHERE NOT(uid=ANY(@OldIds)) ORDER BY uid", parameters)).ToArray();
            evidence.Add(new { Table = table, OldCount = original.Ids.Length, Before = original.Snapshot, After = after, AddedIds = added });
            Assert.AreEqual(original.Snapshot, after, $"{table} 原记录被改变或删除。");
        }
        var sequence = await session.Connection.ExecuteScalarAsync<long>("SELECT last_value FROM public.inventory_ledger_sequence");
        Directory.CreateDirectory("TestResults/postgresql-migration");
        await File.WriteAllTextAsync("TestResults/postgresql-migration/p09-r13-automatic-data-protection.json", JsonSerializer.Serialize(new
        {
            RecordedAt = DateTimeOffset.Now, Database = "dv_next_pg_failure_tests_20261004", BusinessDate,
            SequenceBefore = OriginalSequence, SequenceAfter = sequence, Tables = evidence
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>表名只来自本库目录，使用原身份数组比较完整行；不排除任何原记录或借更新时间猜测归属。</summary>
    private static async Task<string> SnapshotAsync(DbSession session, string table, Guid[] ids)
    {
        var parameters = new PostgreSqlParameters(); parameters.AddIds("OldIds", ids);
        return await session.Connection.ExecuteScalarAsync<string>($"""
            SELECT count(*)::text||':'||md5(COALESCE(string_agg(md5(to_jsonb(t)::text),'' ORDER BY uid),''))
            FROM public.{PostgreSqlNames.Quote(table)} t WHERE uid=ANY(@OldIds)
            """, parameters) ?? throw new AssertFailedException("原记录保护摘要为空。");
    }

    /// <summary>客供件没有自身采购 Link 时按实际库存耗用，整单反向恢复原仓原量。</summary>
    [TestMethod]
    public async Task PackingListConsumesUnlinkedComponentAndRestoresItsStock()
    {
        var connection = ConnectionString();
        using var f = new Fixture(connection);
        var seed = await f.SeedPackingListAsync(includeComponent: true);
        var service = f.PackingCoordinator();
        var preview = await service.PreviewCoreAsync(seed.Packing, 1, BusinessDate, default);
        Assert.AreEqual(0, preview.Blockers.Count);
        Assert.AreEqual(4m, preview.Actions.Single(x => x.Kind == "Consumption").Qty);
        var posted = await service.ExecutePostCommandAsync(new(seed.Packing, Guid.NewGuid(), 1, BusinessDate, preview.Token!), default);
        Assert.AreEqual(3, await f.CountPackingLedgers(seed));
        Assert.AreEqual(0m, await f.Session.Connection.ExecuteScalarAsync<decimal>(
            "SELECT on_hand_qty FROM public.inventory_balance WHERE warehouse_site_id=@Warehouse AND product_id=@Component", seed));
        using var reversing = new Fixture(connection);
        var reverseService = reversing.PackingReversal();
        var reversePreview = await reverseService.ReversePreviewCoreAsync(seed.Packing, posted.Detail!.Header.Version, BusinessDate, "客供件测试恢复", default);
        Assert.AreEqual(0, reversePreview.Blockers.Count);
        await reverseService.ExecuteReverseCommandAsync(new(seed.Packing, Guid.NewGuid(), posted.Detail.Header.Version, BusinessDate, "客供件测试恢复", reversePreview.Token!), default);
        Assert.AreEqual(6, await reversing.CountPackingLedgers(seed));
        Assert.AreEqual(4m, await reversing.Session.Connection.ExecuteScalarAsync<decimal>(
            "SELECT on_hand_qty FROM public.inventory_balance WHERE warehouse_site_id=@Warehouse AND product_id=@Component", seed));
    }

    /// <summary>第二阶段真实协调器和库存端口提交后可从独立连接看到单据、流水、执行记录，再整单反向。</summary>
    [TestMethod]
    public async Task PackingListCoordinatorPostsReplaysAndReversesInRealDatabase()
    {
        var connection = ConnectionString();
        using var f = new Fixture(connection);
        var seed = await f.SeedPackingListAsync();
        var service = f.PackingCoordinator();
        var preview = await service.PreviewCoreAsync(seed.Packing, 1, BusinessDate, default);
        Assert.AreEqual(0, preview.Blockers.Count, string.Join(";", preview.Blockers.Select(x => x.Message)));
        var command = new PackingListPostRequest(seed.Packing, Guid.NewGuid(), 1, BusinessDate, preview.Token!);
        var posted = await service.ExecutePostCommandAsync(command, default);
        Assert.AreEqual(PackingListPostingStatusEnum.Posted, posted.Detail!.Header.PostingStatus);
        using (var verify = new Fixture(connection))
        {
            Assert.AreEqual(0m, await verify.Balance(seed));
            Assert.AreEqual(2, await verify.CountPackingLedgers(seed));
            Assert.AreEqual(1, await verify.CountPackingExecutions(seed));
        }
        Assert.IsTrue((await service.ExecutePostCommandAsync(command, default)).AlreadyApplied);
        Assert.AreEqual(2, await f.CountPackingLedgers(seed));
        // 新请求使用新的 scoped 服务；不在已经准备过的库存作用域上启动第二个事务。
        using var reversing = new Fixture(connection);
        var reverseService = reversing.PackingReversal();
        var reversePreview = await reverseService.ReversePreviewCoreAsync(seed.Packing, posted.Detail.Header.Version, BusinessDate, "测试反向", default);
        Assert.AreEqual(0, reversePreview.Blockers.Count);
        var reverse = new PackingListReverseRequest(seed.Packing, Guid.NewGuid(), posted.Detail.Header.Version, BusinessDate, "测试反向", reversePreview.Token!);
        var reversed = await reverseService.ExecuteReverseCommandAsync(reverse, default);
        Assert.AreEqual(PackingListPostingStatusEnum.Reversed, reversed.Detail!.Header.PostingStatus);
        Assert.IsTrue((await reverseService.ExecuteReverseCommandAsync(reverse, default)).AlreadyApplied);
        using var final = new Fixture(connection);
        Assert.AreEqual(0m, await final.Balance(seed));
        Assert.AreEqual(4, await final.CountPackingLedgers(seed));
        Assert.AreEqual(2, await final.CountPackingExecutions(seed));
    }

    /// <summary>真实库存写入后执行记录失败，独立连接必须看不到任何部分单据和流水。</summary>
    [TestMethod]
    public async Task PackingListExecutionFailureRollsBackRealInventoryAndStatus()
    {
        var connection = ConnectionString();
        using var f = new Fixture(connection);
        var seed = await f.SeedPackingListAsync();
        var service = f.PackingCoordinator(failEvidence: true);
        var preview = await service.PreviewCoreAsync(seed.Packing, 1, BusinessDate, default);
        Assert.AreEqual(0, preview.Blockers.Count);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => service.ExecutePostCommandAsync(
            new(seed.Packing, Guid.NewGuid(), 1, BusinessDate, preview.Token!), default));
        using var verify = new Fixture(connection);
        Assert.AreEqual(0m, await verify.Balance(seed));
        Assert.AreEqual(0, await verify.CountPackingLedgers(seed));
        Assert.AreEqual(0, await verify.CountPackingExecutions(seed));
        Assert.AreEqual(0, await verify.Session.Connection.ExecuteScalarAsync<int>(
            "SELECT posting_status FROM public.packing_list_head WHERE uid=@Packing", seed));
        Assert.AreEqual(0, await verify.Session.Connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM public.inbound_document WHERE trigger_packing_list_id=@Packing", seed));
        Assert.AreEqual(0, await verify.Session.Connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM public.outbound_document WHERE trigger_packing_list_id=@Packing", seed));
    }

    /// <summary>两个真实连接竞争同一来源，已发十件后另一个十件箱单必须失败且不留下部分库存写入。</summary>
    [TestMethod]
    public async Task CompetingPackingListsCannotDeliverSameSourceTwice()
    {
        var connection = ConnectionString();
        using var first = new Fixture(connection);
        var seed = await first.SeedPackingListAsync();
        var secondId = await first.ClonePackingDraftAsync(seed);
        using var second = new Fixture(connection);
        var one = first.PackingCoordinator(); var two = second.PackingCoordinator();
        var previewOne = await one.PreviewCoreAsync(seed.Packing, 1, BusinessDate, default);
        var previewTwo = await two.PreviewCoreAsync(secondId, 1, BusinessDate, default);
        Assert.AreEqual(0, previewOne.Blockers.Count); Assert.AreEqual(0, previewTwo.Blockers.Count);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        async Task<bool> Attempt(PackingListPostingService service, Guid id, string token)
        {
            try { await service.ExecutePostCommandAsync(new(id, Guid.NewGuid(), 1, BusinessDate, token), timeout.Token); return true; }
            catch (PackingListPostingFailure) { return false; }
        }
        var results = await Task.WhenAll(Task.Run(() => Attempt(one, seed.Packing, previewOne.Token!)),
            Task.Run(() => Attempt(two, secondId, previewTwo.Token!)));
        Assert.AreEqual(1, results.Count(x => x));
        using var check = new Fixture(connection);
        Assert.AreEqual(10m, await check.Session.Connection.ExecuteScalarAsync<decimal>("""
            SELECT COALESCE(SUM(l.qty),0) FROM public.packing_list_line l
            JOIN public.packing_list_head h ON h.uid=l.packing_list_head_id
            WHERE h.uid=ANY(@Ids) AND h.posting_status=1 AND h.is_deleted=FALSE AND l.is_deleted=FALSE;
            """, new { Ids = new[] { seed.Packing, secondId } }));
        Assert.AreEqual(1, await check.Session.Connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM public.packing_list_posting_execution WHERE packing_list_head_id=ANY(@Ids)",
            new { Ids = new[] { seed.Packing, secondId } }));
        Assert.AreEqual(0m, await check.Balance(seed));
    }

    /// <summary>真实连接同事务可见新增余额，外层回滚同时撤销单据、流水及余额。</summary>
    [TestMethod]
    public async Task InboundThenOutboundRollbackUsesOneRealTransaction()
    {
        using var f = Open();
        var seed = new Seed();
        var input = seed.Request(true, 10);
        var output = seed.Request(false, 10);
        await using var lease = await f.PrepareCommandsAsync(seed, [input, output]);
        Assert.IsNotNull(lease, "Serializable 自动端口必须沿用实际事务前命令租约。");
        f.Session.Transaction = f.Session.Connection.BeginTransaction(IsolationLevel.Serializable);
        await f.SeedAsync(seed);
        await f.Scope.PrepareAsync(seed.Resources(input, output));
        await f.In.CreateAndPostAsync(input);
        Assert.AreEqual(10m, await f.Balance(seed));
        await f.Out.CreateAndPostAsync(output);
        Assert.AreEqual(0m, await f.Balance(seed));
        Assert.AreEqual(2, await f.Session.Connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM public.inventory_ledger_entry WHERE product_id=@Product", seed, f.Session.Transaction));
        f.Session.Transaction.Rollback(); f.Session.Transaction.Dispose(); f.Session.Transaction = null;
        Assert.AreEqual(0, await f.Session.Connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM public.inventory_ledger_entry WHERE product_id=@Product", seed));
        Assert.AreEqual(0, await f.Session.Connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM public.inbound_document WHERE trigger_packing_list_id=@Packing", seed));
    }

    /// <summary>并发竞争相同采购容量与缺失余额键，只允许一个六件入库消费十件容量。</summary>
    [TestMethod]
    public async Task ConcurrentReceiptsShareCapacityAndMissingBalanceKey()
    {
        var connection = ConnectionString();
        Seed seed;
        using (var setup = new Fixture(connection))
        {
            setup.Session.Transaction = setup.Session.Connection.BeginTransaction();
            seed = await setup.SeedAsync();
            setup.Session.Transaction.Commit(); setup.Session.Transaction.Dispose(); setup.Session.Transaction = null;
        }
        // 专用测试库保留本次唯一身份的证据，不清空表或删除用户数据。
        async Task<bool> Attempt()
        {
            using var f = new Fixture(connection);
            var input = seed.Request(true, 6);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            await using var lease = await f.PrepareCommandsAsync(seed, [input], timeout.Token);
            Assert.IsNotNull(lease, "并发子命令必须属于既有事务前父/子租约。");
            f.Session.Transaction = f.Session.Connection.BeginTransaction(IsolationLevel.Serializable);
            try
            {
                await f.Scope.PrepareAsync(seed.Resources(input), timeout.Token);
                await f.In.CreateAndPostAsync(input, timeout.Token);
                f.Session.Transaction!.Commit(); f.Session.Transaction.Dispose(); f.Session.Transaction = null;
                return true;
            }
            catch (AutomaticPostingException ex) when (ex.Code == "PURCHASE_CAPACITY_EXCEEDED") { return false; }
            // 保留原 Serializable；PG明确中止旧快照事务也属于本次竞争的失败方，不能重试或留下部分库存。
            catch (PostgresException ex) when (ex.SqlState == "40001") { return false; }
        }
        var results = await Task.WhenAll(Task.Run(Attempt), Task.Run(Attempt));
        Assert.AreEqual(1, results.Count(x => x));
        using var check = new Fixture(connection);
        Assert.AreEqual(6m, await check.Balance(seed));
    }

    private static Fixture Open() => new(ConnectionString());

    /// <summary>复用现有 PG 故障库显式门禁和实例/库白名单，不能降级到运行、原库或备份，不输出凭据。</summary>
    private static string ConnectionString() => PostgreSqlIntegrationEnvironment.GetFailureConnectionString();

    /// <summary>将真实 DbSession、仓储、编号器和库存内核组合，不使用业务写入替身。</summary>
    private sealed class Fixture : IDisposable
    {
        public DbSession Session { get; }
        public AutomaticPostingScope Scope { get; }
        public IInboundPostingPort In { get; }
        public IOutboundPostingPort Out { get; }
        private readonly RepositoryBaseContext _repository;

        /// <summary>组合真实第二阶段仓储和第一阶段端口；只有权限许可和指定失败点使用替身。</summary>
        public PackingListPostingService PackingCoordinator(bool failEvidence = false)
        {
            IPackingListExecutionRepository execution = new PostgreSqlPackingListExecutionRepository(_repository);
            if (failEvidence)
            {
                var real = execution;
                execution = Proxy.Make<IPackingListExecutionRepository>((m, a) => m switch {
                    "IsSchemaReadyAsync" => real.IsSchemaReadyAsync((CancellationToken)a[0]!),
                    "FindCommandAsync" => real.FindCommandAsync((Guid)a[0]!, (CancellationToken)a[1]!),
                    "MarkAsync" => real.MarkAsync((PackingListHead)a[0]!, (int)a[1]!, (bool)a[2]!, (CancellationToken)a[3]!),
                    "AppendAsync" => throw new InvalidOperationException("显式测试故障：库存写入后执行记录失败。"),
                    _ => throw new NotSupportedException(m) });
            }
            var permissions = Proxy.Make<IPermissionEvaluationService>((m, _) => m == "HasPermissionAsync" ? Task.FromResult(true) : throw new NotSupportedException(m));
            return new(new ServiceBaseContext(Session, NullLoggerFactory.Instance), new PostgreSqlPackingListRepository(_repository),
                new PostgreSqlPackingListPostingFactsRepository(_repository), execution, new PostgreSqlInboundSourceRepository(_repository), Scope, In, Out,
                new PostgreSqlBusinessOperationLogRepository(_repository), new TestUser(), permissions,
                new SwiftExport.Application.Commands.BusinessCommandExecutor(
                    new ServiceBaseContext(Session, NullLoggerFactory.Instance),
                    new SwiftExport.Infrastructure.Repositories.Commands.PostgreSqlCommandReceiptRepository(_repository), new TestUser()),
                null!, null!);
        }

        /// <summary>组合真实反向证据仓储、自动端口和公共命令回执执行器。</summary>
        public SwiftExport.Application.Services.Shipping.PackingLists.PackingListReversalService PackingReversal()
        {
            var user = new TestUser();
            var permissions = Proxy.Make<IPermissionEvaluationService>((m, _) => m == "HasPermissionAsync"
                ? Task.FromResult(true) : throw new NotSupportedException(m));
            return new(new ServiceBaseContext(Session, NullLoggerFactory.Instance), new PostgreSqlPackingListRepository(_repository),
                new PostgreSqlPackingListPostingFactsRepository(_repository), new PostgreSqlPackingListExecutionRepository(_repository),
                Scope, In, Out, new PostgreSqlBusinessOperationLogRepository(_repository), user, permissions,
                new SwiftExport.Application.Commands.BusinessCommandExecutor(
                    new ServiceBaseContext(Session, NullLoggerFactory.Instance),
                    new SwiftExport.Infrastructure.Repositories.Commands.PostgreSqlCommandReceiptRepository(_repository), user),
                null!, null!);
        }

        /// <summary>创建唯一前缀的普通成品箱单种子并提交，供外层协调器独占下一事务；不删除测试证据。</summary>
        public async Task<Seed> SeedPackingListAsync(bool includeComponent = false)
        {
            Session.Transaction = Session.Connection.BeginTransaction();
            var seed = await SeedAsync();
            var customer = new Customer { CustomerCode = "PLTEST-" + Guid.NewGuid().ToString("N"), DocRelateNo = Guid.NewGuid().ToString("N") };
            await Add(customer);
            var source = new GrossDemand { CustomerId = customer.Uid, SourceType = "SO", SourceUid = Guid.NewGuid(), SourceLineId = Guid.NewGuid(),
                SourceNo = "PLTEST", ProductId = seed.Product, UnitSnapshot = "PCS", DemandQty = 10, DemandBasisFingerprint = "test-basis" };
            await Add(source);
            var root = new NetDemand { GrossDemandId = source.Uid, ProductId = seed.Product, GrossQty = 10,
                SourceDemandBasisFingerprintSnapshot = "test-basis", DecompositionRevision = 1, LineNumber = 1 };
            root.RootNetDemandId = root.Uid;
            await Add(root);
            if (includeComponent)
            {
                await Add(new Product { Uid = seed.Component, Sku = "AUTO-COMP-" + seed.Component.ToString("N"), Unit = "PCS", MaterialType = "客供件", SupplyType = "采购" });
                await Add(new NetDemand { GrossDemandId = source.Uid, ProductId = seed.Component, GrossQty = 4, LineNumber = 2,
                    ParentNetDemandId = root.Uid, RootNetDemandId = root.Uid, DeliveryTargetNetDemandId = root.Uid,
                    DeliveryMode = DemandDeliveryModeEnum.ParentExecutionSupplier, MaterialTypeSnapshot = "客供件", DecompositionRevision = 1 });
                // 期初库存测试夹具，不伪造采购 Link；正式被测耗用和反向都必须走库存内核。
                await Add(new InventoryBalance { WarehouseSiteId = seed.Warehouse, ProductId = seed.Component, UnitSnapshot = "PCS", OnHandQty = 4 });
            }
            var purchaseHead = await Session.Connection.ExecuteScalarAsync<Guid>(
                "SELECT purchase_order_id FROM public.purchase_order_line WHERE uid=@Purchase", seed, Session.Transaction);
            await Add(new DemandExecutionLink { CommandItemId = Guid.NewGuid(), NetDemandId = root.Uid, TargetType = "PO", TargetHeadId = purchaseHead, TargetLineId = seed.Purchase, AllocatedQty = 10 });
            await Add(new PackingListHead { Uid = seed.Packing, PackingListNo = "PLTEST-" + seed.Packing.ToString("N"),
                CustomerId = customer.Uid, InvoiceDate = BusinessDate, CreateCommandId = Guid.NewGuid() });
            await Add(new PackingListLine { PackingListHeadId = seed.Packing, LineNumber = 1, GrossDemandId = source.Uid,
                ProductId = seed.Product, UnitSnapshot = "PCS", Qty = 10 });
            Session.Transaction.Commit(); Session.Transaction.Dispose(); Session.Transaction = null;
            return seed;
        }

        /// <summary>独立连接统计箱单实际单据关联的所有原始和反向流水。</summary>
        public Task<int> CountPackingLedgers(Seed seed) => Session.Connection.ExecuteScalarAsync<int>("""
            SELECT COUNT(*) FROM public.inventory_ledger_entry
            WHERE source_document_id IN (SELECT uid FROM public.inbound_document WHERE trigger_packing_list_id=@Packing
                UNION ALL SELECT uid FROM public.outbound_document WHERE trigger_packing_list_id=@Packing);
            """, seed, Session.Transaction);
        /// <summary>读取实际持久化执行头数量，不使用内存结果替代。</summary>
        public Task<int> CountPackingExecutions(Seed seed) => Session.Connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM public.packing_list_posting_execution WHERE packing_list_head_id=@Packing", seed, Session.Transaction);

        /// <summary>创建引用相同来源的第二份草稿，验证草稿不预占交付容量。</summary>
        public async Task<Guid> ClonePackingDraftAsync(Seed seed)
        {
            var detail = await new PostgreSqlPackingListRepository(_repository).GetDetailAsync(seed.Packing);
            Assert.IsNotNull(detail);
            var head = new PackingListHead { PackingListNo = "PLTEST-" + Guid.NewGuid().ToString("N"),
                CustomerId = detail.Header.CustomerId, InvoiceDate = BusinessDate, CreateCommandId = Guid.NewGuid() };
            await Add(head);
            await Add(new PackingListLine { PackingListHeadId = head.Uid, LineNumber = 1, GrossDemandId = detail.Lines.Single().GrossDemandId,
                ProductId = seed.Product, UnitSnapshot = "PCS", Qty = 10 });
            return head.Uid;
        }

        /// <summary>同一显式 PG 会话组合已有真实仓储、库存内核及编号器，不接入默认配置或第二事务。</summary>
        public Fixture(string connection)
        {
            Session = new(new PostgreSqlConnectionFactory(connection));
            var user = new TestUser();
            _repository = new(Session, NullLoggerFactory.Instance, new AuditStampService(user), user);
            var context = new ServiceBaseContext(Session, NullLoggerFactory.Instance);
            var input = new PostgreSqlInboundDocumentRepository(_repository);
            var executionReads = new PostgreSqlInboundExecutionReadRepository(_repository);
            var sources = new PostgreSqlInboundSourceRepository(_repository);
            var output = new PostgreSqlOutboundDocumentRepository(_repository);
            var outputReads = new PostgreSqlOutboundExecutionReadRepository(_repository);
            var stocks = new PostgreSqlInventoryPostingRepository(_repository);
            var warehouses = new PostgreSqlDapperRepository<WarehouseSite>(_repository);
            var auto = new PostgreSqlAutomaticDocumentRepository(_repository);
            Scope = new(context, auto, input, executionReads, sources, output, outputReads, stocks, warehouses);
            var ports = new AutomaticPostingPorts(Scope, auto, input, sources, executionReads, output, outputReads,
                new InventoryPostingService(context, stocks, new PostgreSqlWarehouseManagementRepository(_repository)), warehouses, stocks,
                new PostgreSqlBusinessOperationLogRepository(_repository), new DocumentNoService(new PostgreSqlDocumentSequenceRepository(_repository)), user);
            In = ports; Out = ports;
        }

        /// <summary>沿用实际 PG 父/子命令租约，在 Serializable 首次快照前准备原声明身份，不补业务锁或自动重试。</summary>
        public async Task<IAsyncDisposable?> PrepareCommandsAsync(Seed seed, IReadOnlyList<AutomaticPostingRequest> requests, CancellationToken token = default)
        {
            var logs = new PostgreSqlBusinessOperationLogRepository(_repository);
            var lease = await logs.AcquireBeforeTransactionAsync(seed.Correlation, token);
            if (lease is null) return null;
            try
            {
                // 子命令复用外层租约；与实际执行器保持先父、后子、再开始事务的顺序。
                var prepared = await logs.AcquireRelatedBeforeTransactionAsync(
                    seed.Correlation, requests.Select(request => request.CommandId).ToArray(), token);
                if (prepared is null) await lease.DisposeAsync();
                return prepared;
            }
            catch
            {
                await lease.DisposeAsync();
                throw;
            }
        }

        /// <summary>仅插入本场景随机新产品、仓及采购头行；可先声明身份以准备真实租约，十件容量和原采购类型保持。</summary>
        public async Task<Seed> SeedAsync(Seed? prepared = null)
        {
            var seed = prepared ?? new Seed();
            await Add(new Product { Uid = seed.Product, Sku = "AUTO-" + seed.Product.ToString("N"), Unit = "PCS", SupplyType = "采购", MaterialType = "成品" });
            await Add(new WarehouseSite { Uid = seed.Warehouse, Code = "AUTO-" + seed.Warehouse.ToString("N"), Name = "自动接口测试仓", SupplierId = seed.Supplier, IsActive = true, SiteType = WarehouseSiteTypeEnum.SupplierVirtual });
            var head = new PurchaseOrderHead { SupplierId = seed.Supplier, SupplierCode = "TEST", Currency = "CNY", OrderNo = "AUTO-" + seed.Purchase.ToString("N"),
                OrderDate = BusinessDate, ConfirmationStatus = ConfirmationStatusEnum.已确认, ExecutionRequestId = Guid.NewGuid() };
            await Add(head);
            // 数量物理列仅翻译为现有 PG quantity，稳定采购行身份与十件容量保持。
            await Session.Connection.ExecuteAsync("""
                INSERT INTO public.purchase_order_line(uid,purchase_order_id,line_number,order_no,product_id,sku,unit_snapshot,quantity,material_type)
                VALUES(@Purchase,@Head,1,@OrderNo,@Product,'TEST','PCS',10,'成品');
                """, new { seed.Purchase, Head = head.Uid, head.OrderNo, seed.Product }, Session.Transaction);
            return seed;
        }

        /// <summary>测试种子以预分配 Uid 关联，按实体列映射直接插入，不借通用 Add 重建身份。</summary>
        private async Task Add<T>(T entity) where T : BaseEntity
        {
            _repository.AuditStamp.StampForInsert(entity);
            var metadata = PostgreSqlEntityMetadata.GetMetadata(typeof(T));
            var columns = string.Join(',', metadata.Columns.Select(column => PostgreSqlNames.Quote(column.ColumnName)));
            var values = string.Join(',', metadata.Columns.Select(column => "@" + column.ParameterName));
            var sql = $"INSERT INTO {PostgreSqlNames.Quote(metadata.SchemaName)}.{PostgreSqlNames.Quote(metadata.TableName)} ({columns}) VALUES ({values});";
            Assert.AreEqual(1, await Session.Connection.ExecuteAsync(sql, PostgreSqlParameters.Entity(entity, metadata), Session.Transaction));
        }
        /// <summary>在原调用方事务读取本场景库存键余额，不将模拟数据当成真实写入结果。</summary>
        public Task<decimal> Balance(Seed seed) => Session.Connection.ExecuteScalarAsync<decimal>(
            "SELECT COALESCE(SUM(on_hand_qty),0) FROM public.inventory_balance WHERE warehouse_site_id=@Warehouse AND product_id=@Product", seed, Session.Transaction);
        public void Dispose() => Session.Dispose();
    }

    private sealed class Seed
    {
        public Guid Warehouse { get; } = Guid.NewGuid();
        public Guid Product { get; } = Guid.NewGuid();
        public Guid Component { get; } = Guid.NewGuid();
        public Guid Supplier { get; } = Guid.NewGuid();
        public Guid Purchase { get; } = Guid.NewGuid();
        public Guid Packing { get; } = Guid.NewGuid();
        public Guid Correlation { get; } = Guid.NewGuid();
        public AutomaticPostingRequest Request(bool input, decimal qty) => new()
        {
            CommandId = Guid.NewGuid(), CorrelationId = Correlation, PackingListId = Packing, PackingListNo = "PL-TEST", BusinessDate = BusinessDate,
            Groups = [new() { GroupId = Guid.NewGuid(), WarehouseSiteId = Warehouse,
                OutboundType = input ? null : SwiftExport.Contracts.SysEnums.Inventory.OutboundDocumentTypeEnum.FinalCustomerShipment,
                Lines = [new() { LineId = Guid.NewGuid(), BasisId = Guid.NewGuid(), ProductId = Product, Unit = "PCS", Quantity = qty, PurchaseLineId = input ? Purchase : null }] }]
        };
        public AutomaticPostingResources Resources(params AutomaticPostingRequest[] requests) => new()
        {
            CorrelationId = Correlation, PackingListId = Packing, PurchaseLineIds = [Purchase],
            CommandIds = requests.Select(x => x.CommandId).ToArray(), StockKeys = [new(Warehouse, Product)],
            InboundDocumentIds = requests.SelectMany(x => x.Groups).Where(x => x.OutboundType is null).Select(x => x.GroupId).ToArray(),
            OutboundDocumentIds = requests.SelectMany(x => x.Groups).Where(x => x.OutboundType.HasValue).Select(x => x.GroupId).ToArray(),
            CommandItemIds = requests.SelectMany(x => x.Groups).SelectMany(x => x.Lines).Select(x => x.LineId).ToArray()
        };
    }

    private sealed class TestUser : ICurrentUser
    {
        public string UserName => "AutomaticPostingSqlTest";
        public Guid? UserId => Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        public bool IsAuthenticated => true;
    }
}
