using System.Text.RegularExpressions;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SwiftExport.Application;
using SwiftExport.Application.Interfaces.Procurement;
using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderDetailDto;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.Auditing;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Procurement;
using SwiftExport.Infrastructure;

namespace SwiftExport.Application.Tests.Procurement;

/// <summary>只创建唯一命名的隔离 LocalDB 测试库；不读取应用配置、不连接任何既有业务库。</summary>
internal sealed class IndependentProcurementSqlFixture : IAsyncDisposable
{
    private const string Prefix = "SwiftExport_ProcurementTests_";
    private readonly string databaseName = Prefix + Guid.NewGuid().ToString("N");
    private ServiceProvider? provider;
    private bool created;
    public Guid ActorId { get; set; } = Guid.NewGuid();
    public Guid OtherActorId { get; } = Guid.NewGuid();
    public Guid CustomerId { get; } = Guid.NewGuid();
    public Guid SupplierId { get; } = Guid.NewGuid();
    public Guid ReceiptSupplierId { get; } = Guid.NewGuid();
    public Guid ConsumptionSupplierId { get; } = Guid.NewGuid();
    public Guid ReceiptSiteId { get; } = Guid.NewGuid();
    public Guid ConsumptionSiteId { get; } = Guid.NewGuid();
    public Guid ProductId { get; } = Guid.NewGuid();
    public Guid ComponentId { get; } = Guid.NewGuid();
    public Guid BomLineId { get; } = Guid.NewGuid();
    public Guid RoleId { get; } = Guid.NewGuid();
    public Guid LegacyOrderId { get; } = Guid.NewGuid();
    public Guid LegacyLineId { get; } = Guid.NewGuid();

    /// <summary>连接只使用本机 LocalDB 集成认证，不包含密码或环境业务连接字符串。</summary>
    private static string ConnectionString(string database) => new SqlConnectionStringBuilder
    {
        DataSource = @"(LocalDB)\MSSQLLocalDB", InitialCatalog = database,
        IntegratedSecurity = true, TrustServerCertificate = true, ConnectTimeout = 30
    }.ConnectionString;

    /// <summary>先执行实体生成的真实空库基线，再插入参数化测试主数据。</summary>
    public async Task InitializeAsync(bool includeLegacyOrder = false)
    {
        RequireOwnedDatabase();
        await using (var master = new SqlConnection(ConnectionString("master")))
        {
            await master.OpenAsync();
            await master.ExecuteAsync($"CREATE DATABASE [{databaseName}];");
            created = true;
        }
        var root = FindRepositoryRoot();
        var baseline = await File.ReadAllTextAsync(Path.Combine(root, "SwiftExport.Infrastructure/Database/Generated/SwiftExport.Schema.sql"));
        await ExecuteAsync(baseline);
        if (includeLegacyOrder) await InsertLegacyOrderAsync();
        await SeedAsync();

        var services = new ServiceCollection();
        // 隔离测试保留真实服务异常，避免只看到通用错误而无法定位 SQL 失败。
        services.AddLogging(builder => builder.AddConsole());
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = ConnectionString(databaseName)
        }).Build());
        services.AddSingleton<ICurrentUser>(new FixtureUser(this));
        services.AddSingleton<ICorrelationContext>(new FixtureCorrelation());
        services.AddInfrastructureServices();
        services.AddApplicationServices();
        provider = services.BuildServiceProvider();
    }

    /// <summary>实体生成基线已经包含反向操作所需结构，因此无需追加迁移。</summary>
    public Task PrepareReversalSchemaAsync() => Task.CompletedTask;

    /// <summary>在新列和快照表尚不存在时写入已确认历史采购，检验迁移是否原样保留未知身份与历史数量。</summary>
    private Task<int> InsertLegacyOrderAsync() => ExecuteAsync("""
        INSERT dbo.PurchaseOrderHead(Uid,Version,ConfirmationStatus,OrderNo,CustomerCode,Currency,SupplierId,SupplierCode,SupplierName,
          DemandSourceType,DemandSourceUid,DemandSourceNo,ExecutionRequestId,OrderDate,Remark)
        VALUES(@LegacyOrderId,7,1,N'LEGACY-PO-001',N'UNKNOWN-HISTORICAL-CUSTOMER',N'CNY',@SupplierId,N'OLD-SUP',N'历史供应商名称',
          N'GrossDemand',@LegacyLineId,N'OLD-DEMAND-001',NEWID(),'2025-01-02',N'迁移前原始备注');
        INSERT dbo.PurchaseOrderLine(Uid,Version,PurchaseOrderId,LineNumber,OrderNo,ProductId,Sku,UnitSnapshot,QTY,TaxPrice,TaxAmount,SourceType,SourceNo)
        VALUES(@LegacyLineId,4,@LegacyOrderId,1,N'LEGACY-PO-001',NULL,N'OLD-UNRESOLVED-SKU',NULL,12.25,4,49,N'GrossDemand',N'OLD-DEMAND-001');
        """, new { LegacyOrderId, LegacyLineId, SupplierId });

    /// <summary>为每个调用创建真实 request scope，以验证服务和仓储共享事务。</summary>
    public async Task<T> InScopeAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        using var scope = provider!.CreateScope();
        return await action(scope.ServiceProvider);
    }

    public Task<ServiceResult<ProcurementOrderCommandResult>> CreateAsync(CreateIndependentPurchaseOrderCommand? command = null)
        => InScopeAsync(p => p.GetRequiredService<IProcurementOrderDraftService>().CreateAsync(command ?? CreateCommand()));
    public Task<ServiceResult<ProcurementOrderCommandResult>> SaveAsync(Guid uid, UpdatePurchaseOrderDraftCommand command)
        => InScopeAsync(p => p.GetRequiredService<IProcurementOrderDraftService>().SaveAsync(uid, command));
    public Task<ServiceResult<ProcurementOrderCommandResult>> ConfirmAsync(Guid uid, ConfirmProcurementOrderCommand command)
        => InScopeAsync(p => p.GetRequiredService<IProcurementOrderConfirmationService>().ConfirmAsync(uid, command));

    /// <summary>通过真实事务和确认构建器读取依据，不在测试中重算业务指纹。</summary>
    public Task<ServiceResult<ProcurementConfirmationContextDto>> ContextAsync(Guid uid) => InScopeAsync(p =>
        p.GetRequiredService<IProcurementOrderConfirmationService>().GetConfirmationContextAsync(uid));

    public CreateIndependentPurchaseOrderCommand CreateCommand() => new()
    {
        CommandId = Guid.NewGuid(), CustomerId = CustomerId, SupplierId = SupplierId,
        OrderDate = new DateTime(2026, 8, 31), PackagingRequirements = "客户包装要求",
        Lines = [new() { ProductId = ProductId, Quantity = 10m, UnitPrice = 2m, TaxPrice = 2.5m }]
    };

    public async Task AddBomAsync() => await ExecuteAsync("""
        UPDATE dbo.Product SET BomNo=N'TEST-BOM',BomVersion=1,Version=Version+1 WHERE Uid=@ProductId;
        INSERT dbo.Bom(Uid,BomNo,BomVersion,ParentProductId,ParentSkuSnapshot,LineNumber,ComponentProductId,ComponentSkuSnapshot,Numerator,Denominator)
        VALUES(@BomLineId,N'TEST-BOM',1,@ProductId,N'TEST-FG',1,@ComponentId,N'TEST-B',2,3);
        """, new { ProductId, ComponentId, BomLineId });

    public async Task<int> ExecuteAsync(string sql, object? args = null)
    {
        await using var connection = new SqlConnection(ConnectionString(databaseName));
        await connection.OpenAsync();
        return await connection.ExecuteAsync(sql, args, commandTimeout: 90);
    }

    public async Task<T> ScalarAsync<T>(string sql, object? args = null)
    {
        await using var connection = new SqlConnection(ConnectionString(databaseName));
        await connection.OpenAsync();
        return (await connection.ExecuteScalarAsync<T>(sql, args))!;
    }

    /// <summary>只向锁一致性测试开放本实例隔离库连接，用于观察真实 SQL 阻塞而不是模拟并发。</summary>
    public async Task<SqlConnection> OpenConnectionAsync()
    {
        var connection = new SqlConnection(ConnectionString(databaseName));
        await connection.OpenAsync();
        return connection;
    }

    /// <summary>通过日志表触发器注入真实 SQL 失败，验证前序写入与回执全部回滚。</summary>
    public Task<int> RejectLogsAsync() => ExecuteAsync("""
        CREATE TRIGGER dbo.TR_Test_RejectLog ON dbo.BusinessOperationLog AFTER INSERT AS
        BEGIN THROW 52999,N'隔离测试日志失败',1; END;
        """);

    private Task<int> SeedAsync() => ExecuteAsync("""
        INSERT dbo.SysRoles(Uid,RoleCode,RoleName,IsActive,IsSystem) VALUES(@RoleId,N'SYSTEM_ADMIN',N'隔离测试管理员',1,1);
        INSERT dbo.SysUser(Uid,UserName,PasswordHash,CredentialVersion,IsActive) VALUES(@ActorId,N'sql-test-admin',N'test-only',1,1),(@OtherActorId,N'sql-test-admin-2',N'test-only',1,1);
        INSERT dbo.SysUser_Roles(Uid,UserId,RoleId) VALUES(NEWID(),@ActorId,@RoleId),(NEWID(),@OtherActorId,@RoleId);
        INSERT dbo.Customer(Uid,CustomerCode,CustomerName,DocRelateNo,DepositRate,LeadTimeDays,IsActive) VALUES(@CustomerId,N'TEST-C',N'归属客户',N'TEST-C',0,0,1);
        INSERT dbo.Supplier(Uid,SupplierCode,SupplierName,DocRelateNo,IsActive) VALUES
          (@SupplierId,N'TEST-ACTUAL',N'实际供应商',N'TEST-ACTUAL',1),(@ReceiptSupplierId,N'TEST-RECEIPT',N'收货供应商',N'TEST-RECEIPT',1),
          (@ConsumptionSupplierId,N'TEST-CONSUME',N'消耗供应商',N'TEST-CONSUME',1);
        INSERT dbo.Product(Uid,Sku,DescriptionZh,Unit,MaterialType,SupplyType,BomVersion,ColorId,IsActive) VALUES
          (@ProductId,N'TEST-FG',N'采购成品',N'PCS',N'成品',N'采购',0,@EmptyId,1),
          (@ComponentId,N'TEST-B',N'客供件',N'PCS',N'客供件',N'采购',0,@EmptyId,1);
        INSERT dbo.SysDictData(Uid,DictCode,DataNameEn,IsSystem,IsEnabled) VALUES(NEWID(),N'Unit',N'PCS',1,1);
        INSERT dbo.WarehouseSite(Uid,Code,Name,SiteType,SupplierId,IsActive) VALUES
          (@ReceiptSiteId,N'TEST-R',N'收货仓',0,@ReceiptSupplierId,1),(@ConsumptionSiteId,N'TEST-C',N'消耗仓',0,@ConsumptionSupplierId,1);
        """, new { ActorId, OtherActorId, RoleId, CustomerId, SupplierId, ReceiptSupplierId, ConsumptionSupplierId, ProductId, ComponentId, ReceiptSiteId, ConsumptionSiteId, EmptyId = Guid.Empty });

    /// <summary>只允许删除本实例创建的唯一测试库，不能由外部输入指定目标。</summary>
    public async ValueTask DisposeAsync()
    {
        if (provider != null) await provider.DisposeAsync();
        if (!created) return;
        RequireOwnedDatabase();
        SqlConnection.ClearAllPools();
        await using var master = new SqlConnection(ConnectionString("master"));
        await master.OpenAsync();
        await master.ExecuteAsync($"ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{databaseName}];", commandTimeout: 90);
        created = false;
    }

    private void RequireOwnedDatabase()
    {
        if (!Regex.IsMatch(databaseName, "^" + Prefix + "[0-9a-f]{32}$", RegexOptions.CultureInvariant))
            throw new InvalidOperationException("拒绝操作非本任务隔离数据库。");
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) return directory.FullName;
        throw new InvalidOperationException("找不到采购测试源码根目录。");
    }

    private sealed class FixtureUser(IndependentProcurementSqlFixture fixture) : ICurrentUser
    {
        public string UserName => "sql-test-admin";
        public Guid? UserId => fixture.ActorId;
        public bool IsAuthenticated => true;
    }
    private sealed class FixtureCorrelation : ICorrelationContext
    {
        public string CorrelationId => "isolated-sql-test";
    }

}
