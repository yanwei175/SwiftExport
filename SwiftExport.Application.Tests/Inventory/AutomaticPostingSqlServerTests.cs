using SwiftExport.Application.Services.Systems.Numbering;
using SwiftExport.Infrastructure.Repositories.Systems.Numbering;
using SwiftExport.Infrastructure.Repositories.Inventory.Inbounds;
using SwiftExport.Infrastructure.Repositories.Inventory.Outbounds;
using SwiftExport.Application.Interfaces.Inventory.AutomaticDocuments;
using SwiftExport.Application.Services.Inventory.Posting;
using SwiftExport.Application.Services.Inventory.AutomaticDocuments;
using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
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

/// <summary>显式启用的独立 SQL Server 集成测试；从不读取应用 Default 连接或部署数据库结构。</summary>
[TestClass]
[DoNotParallelize]
public sealed class AutomaticPostingSqlServerTests
{
    /// <summary>客供件没有自身采购 Link 时按实际库存耗用，整单反向恢复原仓原量。</summary>
    [TestMethod]
    [TestCategory("SqlServerIntegration")]
    public async Task PackingListConsumesUnlinkedComponentAndRestoresItsStock()
    {
        var connection = ConnectionString();
        using var f = new Fixture(connection);
        var seed = await f.SeedPackingListAsync(includeComponent: true);
        var service = f.PackingCoordinator();
        var preview = await service.PreviewCoreAsync(seed.Packing, 1, DateTime.Today, default);
        Assert.AreEqual(0, preview.Blockers.Count);
        Assert.AreEqual(4m, preview.Actions.Single(x => x.Kind == "Consumption").Qty);
        var posted = await service.ExecutePostCommandAsync(new(seed.Packing, Guid.NewGuid(), 1, DateTime.Today, preview.Token!), default);
        Assert.AreEqual(3, await f.CountPackingLedgers(seed));
        Assert.AreEqual(0m, await f.Session.Connection.ExecuteScalarAsync<decimal>(
            "SELECT OnHandQty FROM dbo.InventoryBalance WHERE WarehouseSiteId=@Warehouse AND ProductId=@Component", seed));
        using var reversing = new Fixture(connection);
        var reverseService = reversing.PackingReversal();
        var reversePreview = await reverseService.ReversePreviewCoreAsync(seed.Packing, posted.Detail!.Header.Version, DateTime.Today, "客供件测试恢复", default);
        Assert.AreEqual(0, reversePreview.Blockers.Count);
        await reverseService.ExecuteReverseCommandAsync(new(seed.Packing, Guid.NewGuid(), posted.Detail.Header.Version, DateTime.Today, "客供件测试恢复", reversePreview.Token!), default);
        Assert.AreEqual(6, await reversing.CountPackingLedgers(seed));
        Assert.AreEqual(4m, await reversing.Session.Connection.ExecuteScalarAsync<decimal>(
            "SELECT OnHandQty FROM dbo.InventoryBalance WHERE WarehouseSiteId=@Warehouse AND ProductId=@Component", seed));
    }

    /// <summary>第二阶段真实协调器和库存端口提交后可从独立连接看到单据、流水、执行记录，再整单反向。</summary>
    [TestMethod]
    [TestCategory("SqlServerIntegration")]
    public async Task PackingListCoordinatorPostsReplaysAndReversesInRealDatabase()
    {
        var connection = ConnectionString();
        using var f = new Fixture(connection);
        var seed = await f.SeedPackingListAsync();
        var service = f.PackingCoordinator();
        var preview = await service.PreviewCoreAsync(seed.Packing, 1, DateTime.Today, default);
        Assert.AreEqual(0, preview.Blockers.Count, string.Join(";", preview.Blockers.Select(x => x.Message)));
        var command = new PackingListPostRequest(seed.Packing, Guid.NewGuid(), 1, DateTime.Today, preview.Token!);
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
        var reversePreview = await reverseService.ReversePreviewCoreAsync(seed.Packing, posted.Detail.Header.Version, DateTime.Today, "测试反向", default);
        Assert.AreEqual(0, reversePreview.Blockers.Count);
        var reverse = new PackingListReverseRequest(seed.Packing, Guid.NewGuid(), posted.Detail.Header.Version, DateTime.Today, "测试反向", reversePreview.Token!);
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
    [TestCategory("SqlServerIntegration")]
    public async Task PackingListExecutionFailureRollsBackRealInventoryAndStatus()
    {
        var connection = ConnectionString();
        using var f = new Fixture(connection);
        var seed = await f.SeedPackingListAsync();
        var service = f.PackingCoordinator(failEvidence: true);
        var preview = await service.PreviewCoreAsync(seed.Packing, 1, DateTime.Today, default);
        Assert.AreEqual(0, preview.Blockers.Count);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => service.ExecutePostCommandAsync(
            new(seed.Packing, Guid.NewGuid(), 1, DateTime.Today, preview.Token!), default));
        using var verify = new Fixture(connection);
        Assert.AreEqual(0m, await verify.Balance(seed));
        Assert.AreEqual(0, await verify.CountPackingLedgers(seed));
        Assert.AreEqual(0, await verify.CountPackingExecutions(seed));
        Assert.AreEqual(0, await verify.Session.Connection.ExecuteScalarAsync<int>(
            "SELECT PostingStatus FROM dbo.PackingListHead WHERE Uid=@Packing", seed));
        Assert.AreEqual(0, await verify.Session.Connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.InboundDocument WHERE TriggerPackingListId=@Packing", seed));
        Assert.AreEqual(0, await verify.Session.Connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.OutboundDocument WHERE TriggerPackingListId=@Packing", seed));
    }

    /// <summary>两个真实连接竞争同一来源，已发十件后另一个十件箱单必须失败且不留下部分库存写入。</summary>
    [TestMethod]
    [TestCategory("SqlServerIntegration")]
    public async Task CompetingPackingListsCannotDeliverSameSourceTwice()
    {
        var connection = ConnectionString();
        using var first = new Fixture(connection);
        var seed = await first.SeedPackingListAsync();
        var secondId = await first.ClonePackingDraftAsync(seed);
        using var second = new Fixture(connection);
        var one = first.PackingCoordinator(); var two = second.PackingCoordinator();
        var previewOne = await one.PreviewCoreAsync(seed.Packing, 1, DateTime.Today, default);
        var previewTwo = await two.PreviewCoreAsync(secondId, 1, DateTime.Today, default);
        Assert.AreEqual(0, previewOne.Blockers.Count); Assert.AreEqual(0, previewTwo.Blockers.Count);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        async Task<bool> Attempt(PackingListPostingService service, Guid id, string token)
        {
            try { await service.ExecutePostCommandAsync(new(id, Guid.NewGuid(), 1, DateTime.Today, token), timeout.Token); return true; }
            catch (PackingListPostingFailure) { return false; }
        }
        var results = await Task.WhenAll(Task.Run(() => Attempt(one, seed.Packing, previewOne.Token!)),
            Task.Run(() => Attempt(two, secondId, previewTwo.Token!)));
        Assert.AreEqual(1, results.Count(x => x));
        using var check = new Fixture(connection);
        Assert.AreEqual(10m, await check.Session.Connection.ExecuteScalarAsync<decimal>("""
            SELECT COALESCE(SUM(l.Qty),0) FROM dbo.PackingListLine l
            JOIN dbo.PackingListHead h ON h.Uid=l.PackingListHeadId
            WHERE h.Uid IN @Ids AND h.PostingStatus=1 AND h.IsDeleted=0 AND l.IsDeleted=0;
            """, new { Ids = new[] { seed.Packing, secondId } }));
        Assert.AreEqual(1, await check.Session.Connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.PackingListPostingExecution WHERE PackingListHeadId IN @Ids",
            new { Ids = new[] { seed.Packing, secondId } }));
        Assert.AreEqual(0m, await check.Balance(seed));
    }

    /// <summary>真实连接同事务可见新增余额，外层回滚同时撤销单据、流水及余额。</summary>
    [TestMethod]
    [TestCategory("SqlServerIntegration")]
    public async Task InboundThenOutboundRollbackUsesOneRealTransaction()
    {
        using var f = Open();
        f.Session.Transaction = f.Session.Connection.BeginTransaction(IsolationLevel.Serializable);
        var seed = await f.SeedAsync();
        var input = seed.Request(true, 10);
        var output = seed.Request(false, 10);
        await f.Scope.PrepareAsync(seed.Resources(input, output));
        await f.In.CreateAndPostAsync(input);
        Assert.AreEqual(10m, await f.Balance(seed));
        await f.Out.CreateAndPostAsync(output);
        Assert.AreEqual(0m, await f.Balance(seed));
        Assert.AreEqual(2, await f.Session.Connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.InventoryLedgerEntry WHERE ProductId=@Product", seed, f.Session.Transaction));
        f.Session.Transaction.Rollback(); f.Session.Transaction.Dispose(); f.Session.Transaction = null;
        Assert.AreEqual(0, await f.Session.Connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.InventoryLedgerEntry WHERE ProductId=@Product", seed));
        Assert.AreEqual(0, await f.Session.Connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.InboundDocument WHERE TriggerPackingListId=@Packing", seed));
    }

    /// <summary>并发竞争相同采购容量与缺失余额键，只允许一个六件入库消费十件容量。</summary>
    [TestMethod]
    [TestCategory("SqlServerIntegration")]
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
            f.Session.Transaction = f.Session.Connection.BeginTransaction(IsolationLevel.Serializable);
            var input = seed.Request(true, 6);
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
                await f.Scope.PrepareAsync(seed.Resources(input), timeout.Token);
                await f.In.CreateAndPostAsync(input, timeout.Token);
                f.Session.Transaction!.Commit(); f.Session.Transaction.Dispose(); f.Session.Transaction = null;
                return true;
            }
            catch (AutomaticPostingException ex) when (ex.Code == "PURCHASE_CAPACITY_EXCEEDED") { return false; }
        }
        var results = await Task.WhenAll(Task.Run(Attempt), Task.Run(Attempt));
        Assert.AreEqual(1, results.Count(x => x));
        using var check = new Fixture(connection);
        Assert.AreEqual(6m, await check.Balance(seed));
    }

    private static Fixture Open() => new(ConnectionString());

    /// <summary>显式开关及测试目标保护；非约定前缀库还须逐次提供已获授权的服务器和库名。</summary>
    private static string ConnectionString()
    {
        if (Environment.GetEnvironmentVariable("SWIFTEXPORT_RUN_AUTOMATIC_SQL_TESTS") != "1")
            Assert.Inconclusive("未显式启用独立 SQL Server 自动单据集成测试。");
        var value = Environment.GetEnvironmentVariable("SWIFTEXPORT_AUTOMATIC_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(value)) Assert.Inconclusive("未配置专用测试数据库连接。");
        var builder = new SqlConnectionStringBuilder(value);
        var confirmedServer = Environment.GetEnvironmentVariable("SWIFTEXPORT_AUTOMATIC_CONFIRMED_SERVER");
        var confirmedDatabase = Environment.GetEnvironmentVariable("SWIFTEXPORT_AUTOMATIC_CONFIRMED_DATABASE");
        var explicitlyConfirmed = !string.IsNullOrWhiteSpace(confirmedServer) && !string.IsNullOrWhiteSpace(confirmedDatabase)
            && string.Equals(builder.DataSource, confirmedServer, StringComparison.OrdinalIgnoreCase)
            && string.Equals(builder.InitialCatalog, confirmedDatabase, StringComparison.OrdinalIgnoreCase);
        if (!builder.InitialCatalog.StartsWith("SwiftExport_AutomaticPosting_Test_", StringComparison.OrdinalIgnoreCase) && !explicitlyConfirmed)
            Assert.Fail("非约定前缀测试库必须显式确认完全匹配的服务器与库名，不能自动使用应用连接。");
        return value!;
    }

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
            IPackingListExecutionRepository execution = new PackingListExecutionRepository(_repository);
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
            return new(new ServiceBaseContext(Session, NullLoggerFactory.Instance), new PackingListRepository(_repository),
                new PackingListPostingFactsRepository(_repository), execution, new InboundSourceRepository(_repository), Scope, In, Out,
                new BusinessOperationLogRepository(_repository), new TestUser(), permissions,
                new SwiftExport.Application.Commands.BusinessCommandExecutor(
                    new ServiceBaseContext(Session, NullLoggerFactory.Instance),
                    new SwiftExport.Infrastructure.Repositories.Commands.CommandReceiptRepository(_repository), new TestUser()),
                null!, null!);
        }

        /// <summary>组合真实反向证据仓储、自动端口和公共命令回执执行器。</summary>
        public SwiftExport.Application.Services.Shipping.PackingLists.PackingListReversalService PackingReversal()
        {
            var user = new TestUser();
            var permissions = Proxy.Make<IPermissionEvaluationService>((m, _) => m == "HasPermissionAsync"
                ? Task.FromResult(true) : throw new NotSupportedException(m));
            return new(new ServiceBaseContext(Session, NullLoggerFactory.Instance), new PackingListRepository(_repository),
                new PackingListPostingFactsRepository(_repository), new PackingListExecutionRepository(_repository),
                Scope, In, Out, new BusinessOperationLogRepository(_repository), user, permissions,
                new SwiftExport.Application.Commands.BusinessCommandExecutor(
                    new ServiceBaseContext(Session, NullLoggerFactory.Instance),
                    new SwiftExport.Infrastructure.Repositories.Commands.CommandReceiptRepository(_repository), user),
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
                "SELECT PurchaseOrderId FROM dbo.PurchaseOrderLine WHERE Uid=@Purchase", seed, Session.Transaction);
            await Add(new DemandExecutionLink { CommandItemId = Guid.NewGuid(), NetDemandId = root.Uid, TargetType = "PO", TargetHeadId = purchaseHead, TargetLineId = seed.Purchase, AllocatedQty = 10 });
            await Add(new PackingListHead { Uid = seed.Packing, PackingListNo = "PLTEST-" + seed.Packing.ToString("N"),
                CustomerId = customer.Uid, InvoiceDate = DateTime.Today, CreateCommandId = Guid.NewGuid() });
            await Add(new PackingListLine { PackingListHeadId = seed.Packing, LineNumber = 1, GrossDemandId = source.Uid,
                ProductId = seed.Product, UnitSnapshot = "PCS", Qty = 10 });
            Session.Transaction.Commit(); Session.Transaction.Dispose(); Session.Transaction = null;
            return seed;
        }

        /// <summary>独立连接统计箱单实际单据关联的所有原始和反向流水。</summary>
        public Task<int> CountPackingLedgers(Seed seed) => Session.Connection.ExecuteScalarAsync<int>("""
            SELECT COUNT(*) FROM dbo.InventoryLedgerEntry
            WHERE SourceDocumentId IN (SELECT Uid FROM dbo.InboundDocument WHERE TriggerPackingListId=@Packing
                UNION ALL SELECT Uid FROM dbo.OutboundDocument WHERE TriggerPackingListId=@Packing);
            """, seed, Session.Transaction);
        /// <summary>读取实际持久化执行头数量，不使用内存结果替代。</summary>
        public Task<int> CountPackingExecutions(Seed seed) => Session.Connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.PackingListPostingExecution WHERE PackingListHeadId=@Packing", seed, Session.Transaction);

        /// <summary>创建引用相同来源的第二份草稿，验证草稿不预占交付容量。</summary>
        public async Task<Guid> ClonePackingDraftAsync(Seed seed)
        {
            var detail = await new PackingListRepository(_repository).GetDetailAsync(seed.Packing);
            Assert.IsNotNull(detail);
            var head = new PackingListHead { PackingListNo = "PLTEST-" + Guid.NewGuid().ToString("N"),
                CustomerId = detail.Header.CustomerId, InvoiceDate = DateTime.Today, CreateCommandId = Guid.NewGuid() };
            await Add(head);
            await Add(new PackingListLine { PackingListHeadId = head.Uid, LineNumber = 1, GrossDemandId = detail.Lines.Single().GrossDemandId,
                ProductId = seed.Product, UnitSnapshot = "PCS", Qty = 10 });
            return head.Uid;
        }

        public Fixture(string connection)
        {
            Session = new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                { ["ConnectionStrings:Default"] = connection }).Build());
            var user = new TestUser();
            _repository = new(Session, NullLoggerFactory.Instance, new AuditStampService(user), user);
            var context = new ServiceBaseContext(Session, NullLoggerFactory.Instance);
            var input = new InboundDocumentRepository(_repository);
            var executionReads = new InboundExecutionReadRepository(_repository);
            var sources = new InboundSourceRepository(_repository);
            var output = new OutboundDocumentRepository(_repository);
            var outputReads = new OutboundExecutionReadRepository(_repository);
            var stocks = new InventoryPostingRepository(_repository);
            var warehouses = new DapperRepository<WarehouseSite>(_repository);
            var auto = new AutomaticDocumentRepository(_repository);
            Scope = new(context, auto, input, executionReads, sources, output, outputReads, stocks, warehouses);
            var ports = new AutomaticPostingPorts(Scope, auto, input, sources, executionReads, output, outputReads,
                new InventoryPostingService(context, stocks, new WarehouseManagementRepository(_repository)), warehouses, stocks,
                new BusinessOperationLogRepository(_repository), new DocumentNoService(new DocumentSequenceRepository(_repository)), user);
            In = ports; Out = ports;
        }

        public async Task<Seed> SeedAsync()
        {
            var seed = new Seed();
            await Add(new Product { Uid = seed.Product, Sku = "AUTO-" + seed.Product.ToString("N"), Unit = "PCS", SupplyType = "采购", MaterialType = "成品" });
            await Add(new WarehouseSite { Uid = seed.Warehouse, Code = "AUTO-" + seed.Warehouse.ToString("N"), Name = "自动接口测试仓", SupplierId = seed.Supplier, IsActive = true, SiteType = WarehouseSiteTypeEnum.SupplierVirtual });
            var head = new PurchaseOrderHead { SupplierId = seed.Supplier, SupplierCode = "TEST", Currency = "CNY", OrderNo = "AUTO-" + seed.Purchase.ToString("N"),
                OrderDate = DateTime.Today, ConfirmationStatus = ConfirmationStatusEnum.已确认, ExecutionRequestId = Guid.NewGuid() };
            await Add(head);
            // 测试种子遵循采购遗留物理列 QTY，不借通用仓储猜测 Column 映射。
            await Session.Connection.ExecuteAsync("""
                INSERT dbo.PurchaseOrderLine(Uid,PurchaseOrderId,LineNumber,OrderNo,ProductId,Sku,UnitSnapshot,QTY,MaterialType)
                VALUES(@Purchase,@Head,1,@OrderNo,@Product,N'TEST',N'PCS',10,N'成品');
                """, new { seed.Purchase, Head = head.Uid, head.OrderNo, seed.Product }, Session.Transaction);
            return seed;
        }

        /// <summary>测试种子以预分配 Uid 关联，按实体列映射直接插入，不借通用 Add 重建身份。</summary>
        private async Task Add<T>(T entity) where T : BaseEntity
        {
            _repository.AuditStamp.StampForInsert(entity);
            var properties = typeof(T).GetProperties().Where(p => p.CanRead && p.CanWrite
                && !Attribute.IsDefined(p, typeof(System.ComponentModel.DataAnnotations.Schema.NotMappedAttribute))).ToArray();
            var columns = properties.Select(p => "[" + (p.GetCustomAttributes(typeof(System.ComponentModel.DataAnnotations.Schema.ColumnAttribute), false)
                .Cast<System.ComponentModel.DataAnnotations.Schema.ColumnAttribute>().SingleOrDefault()?.Name ?? p.Name) + "]");
            var sql = $"INSERT dbo.[{typeof(T).Name}] ({string.Join(',', columns)}) VALUES ({string.Join(',', properties.Select(p => "@" + p.Name))});";
            Assert.AreEqual(1, await Session.Connection.ExecuteAsync(sql, entity, Session.Transaction));
        }
        public Task<decimal> Balance(Seed seed) => Session.Connection.ExecuteScalarAsync<decimal>(
            "SELECT COALESCE(SUM(OnHandQty),0) FROM dbo.InventoryBalance WHERE WarehouseSiteId=@Warehouse AND ProductId=@Product", seed, Session.Transaction);
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
            CommandId = Guid.NewGuid(), CorrelationId = Correlation, PackingListId = Packing, PackingListNo = "PL-TEST", BusinessDate = DateTime.Today,
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
