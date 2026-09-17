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
using SwiftExport.Infrastructure.Repositories.Systems;
using SwiftExport.Infrastructure.Services.Auth;

namespace SwiftExport.Application.Tests.Inventory;

/// <summary>显式启用的独立 SQL Server 集成测试；从不读取应用 Default 连接或部署数据库结构。</summary>
[TestClass]
[DoNotParallelize]
public sealed class AutomaticPostingSqlServerTests
{
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

    /// <summary>双重显式开关及测试库名前缀保护，未配置则跳过，不尝试连接。</summary>
    private static string ConnectionString()
    {
        if (Environment.GetEnvironmentVariable("SWIFTEXPORT_RUN_AUTOMATIC_SQL_TESTS") != "1")
            Assert.Inconclusive("未显式启用独立 SQL Server 自动单据集成测试。");
        var value = Environment.GetEnvironmentVariable("SWIFTEXPORT_AUTOMATIC_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(value)) Assert.Inconclusive("未配置专用测试数据库连接。");
        var builder = new SqlConnectionStringBuilder(value);
        if (!builder.InitialCatalog.StartsWith("SwiftExport_AutomaticPosting_Test_", StringComparison.OrdinalIgnoreCase))
            Assert.Fail("只允许 SwiftExport_AutomaticPosting_Test_ 前缀的独立测试库。");
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

        public Fixture(string connection)
        {
            Session = new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                { ["ConnectionStrings:Default"] = connection }).Build());
            var user = new TestUser();
            _repository = new(Session, NullLoggerFactory.Instance, new AuditStampService(user), user);
            var context = new ServiceBaseContext(Session, NullLoggerFactory.Instance);
            var input = new InboundManagerRepository(_repository);
            var output = new OutboundManagerRepository(_repository);
            var stocks = new InventoryRepository(_repository);
            var warehouses = new DapperRepository<WarehouseSite>(_repository);
            var auto = new AutomaticDocumentRepository(_repository, new DapperRepository<InboundDocument>(_repository),
                new DapperRepository<InboundDocumentLine>(_repository), new DapperRepository<OutboundDocument>(_repository), new DapperRepository<OutboundDocumentLine>(_repository));
            Scope = new(context, auto, input, output, stocks, warehouses);
            var ports = new AutomaticPostingPorts(Scope, auto, input, output,
                new InventoryPostingService(context, stocks, new WarehouseSiteRepository(_repository)), warehouses, stocks,
                new BusinessOperationLogRepository(_repository), new DocumentNoService(new DocumentSequenceRepository(_repository)), user);
            In = ports; Out = ports;
        }

        public async Task<Seed> SeedAsync()
        {
            var seed = new Seed();
            await Add(new Product { Uid = seed.Product, Sku = "AUTO-" + seed.Product.ToString("N"), Unit = "PCS", SupplyType = "采购", MaterialType = "成品" });
            await Add(new WarehouseSite { Uid = seed.Warehouse, Code = "AUTO-" + seed.Warehouse.ToString("N"), Name = "自动接口测试仓", SupplierId = seed.Supplier, IsActive = true });
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

        private async Task Add<T>(T entity) where T : BaseEntity
        { var result = await new DapperRepository<T>(_repository).AddAsync(entity); Assert.IsTrue(result.Success, result.Message); }
        public Task<decimal> Balance(Seed seed) => Session.Connection.ExecuteScalarAsync<decimal>(
            "SELECT COALESCE(SUM(OnHandQty),0) FROM dbo.InventoryBalance WHERE WarehouseSiteId=@Warehouse AND ProductId=@Product", seed, Session.Transaction);
        public void Dispose() => Session.Dispose();
    }

    private sealed class Seed
    {
        public Guid Warehouse { get; } = Guid.NewGuid();
        public Guid Product { get; } = Guid.NewGuid();
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
