using System.Text.RegularExpressions;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SwiftExport.Application;
using SwiftExport.Application.Interfaces.Procurement;
using SwiftExport.Application.Services.Procurement.OrderManagement;
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

    /// <summary>先执行真实空库初始化，再补测试主数据并执行真实采购迁移。</summary>
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
        var baseline = await File.ReadAllTextAsync(Path.Combine(root, "SwiftExport.Infrastructure/Database/FreshInstall/030_初始化目标业务表.sql"));
        baseline = baseline.Replace("@ExpectedDatabaseName sysname=N'dv_next'", $"@ExpectedDatabaseName sysname=N'{databaseName}'");
        await ExecuteAsync(baseline);
        await ExecuteAsync(MasterSchema);
        if (includeLegacyOrder) await InsertLegacyOrderAsync();
        await ApplyMigrationAsync();
        await SeedAsync();

        var services = new ServiceCollection();
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

    /// <summary>重复执行真实迁移以验证对象创建幂等；仍强制绑定本实例隔离库。</summary>
    public async Task ApplyMigrationAsync()
    {
        var migration = await File.ReadAllTextAsync(Path.Combine(FindRepositoryRoot(), "SwiftExport.Infrastructure/Migrations/020_独立采购执行.sql"));
        migration = Regex.Replace(migration, @"(@ExpectedDatabaseName\s+sysname\s*=\s*)NULL", $"$1N'{databaseName}'", RegexOptions.IgnoreCase);
        foreach (var batch in Regex.Split(migration, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
            if (!string.IsNullOrWhiteSpace(batch)) await ExecuteAsync(batch);
    }

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
        => InScopeAsync(p => p.GetRequiredService<IProcurementOrderCommandService>().CreateAsync(command ?? CreateCommand()));
    public Task<ServiceResult<ProcurementOrderCommandResult>> SaveAsync(Guid uid, UpdatePurchaseOrderDraftCommand command)
        => InScopeAsync(p => p.GetRequiredService<IProcurementOrderCommandService>().SaveAsync(uid, command));
    public Task<ServiceResult<ProcurementOrderCommandResult>> ConfirmAsync(Guid uid, ConfirmProcurementOrderCommand command)
        => InScopeAsync(p => p.GetRequiredService<IProcurementOrderCommandService>().ConfirmAsync(uid, command));

    /// <summary>通过真实事务和确认构建器读取依据，不在测试中重算业务指纹。</summary>
    public Task<ServiceResult<ProcurementConfirmationContextDto>> ContextAsync(Guid uid) => InScopeAsync(p =>
        p.GetRequiredService<ProcurementOrderTransaction>().ExecuteAsync("Read", async () =>
        {
            var repository = p.GetRequiredService<IProcurementOrderRepository>();
            var head = await repository.GetHeadAsync(uid, default) ?? throw new InvalidOperationException("测试订单不存在。");
            var lines = await repository.GetLinesAsync(uid, default);
            return await p.GetRequiredService<ProcurementConfirmationBuilder>().BuildAsync(head, lines, default);
        }, default));

    public CreateIndependentPurchaseOrderCommand CreateCommand() => new()
    {
        CommandId = Guid.NewGuid(), CustomerId = CustomerId, SupplierId = SupplierId,
        OrderDate = new DateTime(2026, 8, 31), PackagingRequirements = "客户包装要求",
        Lines = [new() { ProductId = ProductId, Quantity = 10m, UnitPrice = 2m, TaxPrice = 2.5m, WarehouseSiteId = ReceiptSiteId }]
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
        INSERT dbo.SysRoles(Uid,RoleName) VALUES(@RoleId,N'Admin');
        INSERT dbo.SysUser(Uid,UserName,IsActive) VALUES(@ActorId,N'sql-test-admin',1),(@OtherActorId,N'sql-test-admin-2',1);
        INSERT dbo.SysUser_Roles(Uid,UserId,RoleId) VALUES(NEWID(),@ActorId,@RoleId),(NEWID(),@OtherActorId,@RoleId);
        INSERT dbo.Customer(Uid,CustomerCode,CustomerName) VALUES(@CustomerId,N'TEST-C',N'归属客户');
        INSERT dbo.Supplier(Uid,SupplierCode,SupplierName) VALUES
          (@SupplierId,N'TEST-ACTUAL',N'实际供应商'),(@ReceiptSupplierId,N'TEST-RECEIPT',N'收货供应商'),
          (@ConsumptionSupplierId,N'TEST-CONSUME',N'消耗供应商');
        INSERT dbo.Product(Uid,Sku,DescriptionZh,Unit,MaterialType,SupplyType,BomVersion) VALUES
          (@ProductId,N'TEST-FG',N'采购成品',N'PCS',N'成品',N'采购',0),
          (@ComponentId,N'TEST-B',N'客供件',N'PCS',N'客供件',N'采购',0);
        INSERT dbo.SysDictData(Uid,DictCode,DataNameEn,IsEnabled) VALUES(NEWID(),N'Unit',N'PCS',1);
        INSERT dbo.WarehouseSite(Uid,Code,Name,SiteType,SupplierId) VALUES
          (@ReceiptSiteId,N'TEST-R',N'收货仓',0,@ReceiptSupplierId),(@ConsumptionSiteId,N'TEST-C',N'消耗仓',0,@ConsumptionSupplierId);
        """, new { ActorId, OtherActorId, RoleId, CustomerId, SupplierId, ReceiptSupplierId, ConsumptionSupplierId, ProductId, ComponentId, ReceiptSiteId, ConsumptionSiteId });

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

    // 只补 FreshInstall 未创建的外部主数据与认证表，字段采用实际仓储读取的真实名称。
    private const string MasterSchema = """
        CREATE TABLE dbo.ProductColor(Uid uniqueidentifier NOT NULL PRIMARY KEY,ColorNameZh nvarchar(300) NULL,IsDeleted bit NOT NULL DEFAULT(0));
        CREATE TABLE dbo.Product(Uid uniqueidentifier NOT NULL PRIMARY KEY,Version int NOT NULL DEFAULT(1),IsDeleted bit NOT NULL DEFAULT(0),IsActive bit NOT NULL DEFAULT(1),CreatedDate datetime2 NULL,CreatedBy nvarchar(100) NULL,UpdatedDate datetime2 NULL,UpdatedBy nvarchar(100) NULL,
          Sku nvarchar(200) NOT NULL,DescriptionZh nvarchar(300) NULL,DescriptionEn nvarchar(300) NULL,MainImage nvarchar(1000) NULL,LinkSku nvarchar(200) NULL,FeaturesZh nvarchar(300) NULL,ColorId uniqueidentifier NULL,Unit nvarchar(100) NULL,MaterialType nvarchar(100) NULL,SupplyType nvarchar(100) NULL,BomNo nvarchar(100) NULL,BomVersion int NOT NULL DEFAULT(0));
        CREATE TABLE dbo.CustomerProduct(Uid uniqueidentifier NOT NULL PRIMARY KEY,CustomerId uniqueidentifier NOT NULL,SkuId uniqueidentifier NULL,CustomerSku nvarchar(200) NULL,IsActive bit NOT NULL DEFAULT(1),IsDeleted bit NOT NULL DEFAULT(0));
        CREATE TABLE dbo.Customer(Uid uniqueidentifier NOT NULL PRIMARY KEY,Version int NOT NULL DEFAULT(1),IsDeleted bit NOT NULL DEFAULT(0),CreatedDate datetime2 NULL,CreatedBy nvarchar(100) NULL,UpdatedDate datetime2 NULL,UpdatedBy nvarchar(100) NULL,
          CustomerCode nvarchar(100) NOT NULL,CustomerName nvarchar(300) NULL,IsActive bit NOT NULL DEFAULT(1));
        CREATE TABLE dbo.Supplier(Uid uniqueidentifier NOT NULL PRIMARY KEY,Version int NOT NULL DEFAULT(1),IsDeleted bit NOT NULL DEFAULT(0),CreatedDate datetime2 NULL,CreatedBy nvarchar(100) NULL,UpdatedDate datetime2 NULL,UpdatedBy nvarchar(100) NULL,
          SupplierCode nvarchar(100) NOT NULL,SupplierName nvarchar(300) NOT NULL,IsActive bit NOT NULL DEFAULT(1));
        CREATE TABLE dbo.SysDictData(Uid uniqueidentifier NOT NULL PRIMARY KEY,Version int NOT NULL DEFAULT(1),IsDeleted bit NOT NULL DEFAULT(0),DictCode nvarchar(100) NOT NULL,DataNameEn nvarchar(100) NULL,IsEnabled bit NOT NULL);
        CREATE TABLE dbo.SysUser(Uid uniqueidentifier NOT NULL PRIMARY KEY,UserName nvarchar(100) NOT NULL,IsActive bit NOT NULL,IsDeleted bit NOT NULL DEFAULT(0));
        CREATE TABLE dbo.SysRoles(Uid uniqueidentifier NOT NULL PRIMARY KEY,RoleName nvarchar(100) NOT NULL,IsDeleted bit NOT NULL DEFAULT(0));
        CREATE TABLE dbo.SysUser_Roles(Uid uniqueidentifier NOT NULL PRIMARY KEY,UserId uniqueidentifier NOT NULL,RoleId uniqueidentifier NOT NULL,IsDeleted bit NOT NULL DEFAULT(0));
        CREATE TABLE dbo.SysUser_ModulePermissions(Uid uniqueidentifier NOT NULL PRIMARY KEY,Version int NOT NULL DEFAULT(1),IsDeleted bit NOT NULL DEFAULT(0),CreatedDate datetime2 NULL,CreatedBy nvarchar(256) NULL,UpdatedDate datetime2 NULL,UpdatedBy nvarchar(256) NULL,UserId uniqueidentifier NOT NULL,PermissionKey nvarchar(128) NOT NULL);
        """;
}
