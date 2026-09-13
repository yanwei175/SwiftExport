using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Inventory;

namespace SwiftExport.Application.Tests.Inventory;

/// <summary>在随机 LocalDB 隔离库中用真实 Infrastructure 注册验证库存查询仓储。</summary>
internal sealed class InventoryRepositorySqlFixture : IAsyncDisposable
{
    private const string DatabasePrefix = "SwiftExport_InventoryQueryTests_";
    private readonly string databaseName = DatabasePrefix + Guid.NewGuid().ToString("N");
    private ServiceProvider? provider;
    private bool created;

    /// <summary>测试供应商身份。</summary>
    public Guid SupplierId { get; } = Guid.NewGuid();

    /// <summary>测试虚拟仓身份。</summary>
    public Guid WarehouseId { get; } = Guid.NewGuid();

    /// <summary>测试余额身份集合，顺序与测试数据定义一致。</summary>
    public IReadOnlyList<Guid> BalanceIds { get; private set; } = [];

    private static string ConnectionString(string database) => new SqlConnectionStringBuilder
    {
        DataSource = @"(LocalDB)\MSSQLLocalDB",
        InitialCatalog = database,
        IntegratedSecurity = true,
        TrustServerCertificate = true,
        ConnectTimeout = 15
    }.ConnectionString;

    /// <summary>创建随机隔离库、真实列名的最小库存结构和特殊字符样本。</summary>
    public async Task InitializeAsync()
    {
        RequireOwnedDatabase();
        await using (var master = new SqlConnection(ConnectionString("master")))
        {
            await master.OpenAsync();
            await master.ExecuteAsync($"CREATE DATABASE [{databaseName}];");
            created = true;
        }

        await ExecuteAsync("""
            CREATE TABLE dbo.Supplier
            (
                Uid uniqueidentifier NOT NULL PRIMARY KEY,
                SupplierCode nvarchar(100) NOT NULL,
                SupplierName nvarchar(300) NOT NULL,
                IsActive bit NOT NULL,
                IsDeleted bit NOT NULL
            );
            CREATE TABLE dbo.WarehouseSite
            (
                Uid uniqueidentifier NOT NULL PRIMARY KEY,
                Version int NOT NULL,IsDeleted bit NOT NULL,CreatedDate datetime2(7) NULL,CreatedBy nvarchar(100) NULL,
                UpdatedDate datetime2(7) NULL,UpdatedBy nvarchar(100) NULL,Code nvarchar(100) NOT NULL,Name nvarchar(300) NOT NULL,
                SiteType tinyint NOT NULL,SupplierId uniqueidentifier NULL,SupplierCodeSnapshot nvarchar(100) NULL,
                SupplierNameSnapshot nvarchar(300) NULL,IsActive bit NOT NULL
            );
            CREATE TABLE dbo.InventoryBalance
            (
                Uid uniqueidentifier NOT NULL PRIMARY KEY,
                Version int NOT NULL,IsDeleted bit NOT NULL,CreatedDate datetime2(7) NULL,CreatedBy nvarchar(100) NULL,
                UpdatedDate datetime2(7) NULL,UpdatedBy nvarchar(100) NULL,WarehouseSiteId uniqueidentifier NOT NULL,
                ProductId uniqueidentifier NOT NULL,SkuSnapshot nvarchar(200) NULL,ProductNameSnapshot nvarchar(500) NULL,
                UnitSnapshot nvarchar(100) NULL,OnHandQty decimal(18,4) NOT NULL,LastLedgerSequence bigint NOT NULL
            );
            CREATE TABLE dbo.InventoryLedgerEntry
            (
                Uid uniqueidentifier NOT NULL PRIMARY KEY,
                Version int NOT NULL,IsDeleted bit NOT NULL,CreatedDate datetime2(7) NULL,CreatedBy nvarchar(100) NULL,
                UpdatedDate datetime2(7) NULL,UpdatedBy nvarchar(100) NULL,Sequence bigint NOT NULL,
                WarehouseSiteId uniqueidentifier NOT NULL,ProductId uniqueidentifier NOT NULL,TransactionType int NOT NULL,
                QuantityDelta decimal(18,4) NOT NULL,BalanceAfter decimal(18,4) NOT NULL,
                SourceDocumentType nvarchar(100) NOT NULL,SourceDocumentId uniqueidentifier NULL,SourceLineId uniqueidentifier NULL,
                SourceDocumentNoSnapshot nvarchar(100) NULL,ReversalOfLedgerId uniqueidentifier NULL,
                ReplacementOfLedgerId uniqueidentifier NULL,CorrectionGroupId uniqueidentifier NULL,CommandId uniqueidentifier NOT NULL,
                CommandItemId uniqueidentifier NOT NULL,RequestFingerprint char(64) NULL,Reason nvarchar(2000) NULL,
                OccurredDate datetime2(7) NOT NULL,WarehouseCodeSnapshot nvarchar(100) NULL,
                WarehouseNameSnapshot nvarchar(300) NULL,SkuSnapshot nvarchar(200) NULL,
                ProductNameSnapshot nvarchar(500) NULL,UnitSnapshot nvarchar(100) NULL
            );
            """);

        var samples = new[]
        {
            new Sample("SKU-普通", "DOC-普通"),
            new Sample("SKU-%-百分号", "DOC-%-百分号"),
            new Sample("SKU-_-下划线", "DOC-_-下划线"),
            new Sample("SKU-[-方括号", "DOC-[-方括号"),
            new Sample(@"SKU-\-反斜杠", @"DOC-\-反斜杠")
        };
        BalanceIds = samples.Select(_ => Guid.NewGuid()).ToArray();
        await ExecuteAsync("""
            INSERT dbo.Supplier(Uid,SupplierCode,SupplierName,IsActive,IsDeleted)
            VALUES(@SupplierId,N'SUP-QUERY',N'库存查询供应商',1,0);
            INSERT dbo.WarehouseSite(Uid,Version,IsDeleted,Code,Name,SiteType,SupplierId,SupplierCodeSnapshot,SupplierNameSnapshot,IsActive)
            VALUES(@WarehouseId,1,0,N'WH-QUERY',N'库存查询仓',0,@SupplierId,N'SUP-QUERY',N'库存查询供应商',1);
            """, new { SupplierId, WarehouseId });

        for (var index = 0; index < samples.Length; index++)
        {
            var sample = samples[index];
            var productId = Guid.NewGuid();
            await ExecuteAsync("""
                INSERT dbo.InventoryBalance
                    (Uid,Version,IsDeleted,UpdatedDate,WarehouseSiteId,ProductId,SkuSnapshot,ProductNameSnapshot,UnitSnapshot,OnHandQty,LastLedgerSequence)
                VALUES(@BalanceId,1,0,SYSDATETIME(),@WarehouseId,@ProductId,@Sku,N'库存查询产品',N'PCS',@Quantity,@Sequence);
                INSERT dbo.InventoryLedgerEntry
                    (Uid,Version,IsDeleted,CreatedDate,CreatedBy,Sequence,WarehouseSiteId,ProductId,TransactionType,QuantityDelta,BalanceAfter,
                     SourceDocumentType,SourceDocumentId,SourceLineId,SourceDocumentNoSnapshot,CommandId,CommandItemId,OccurredDate,
                     WarehouseCodeSnapshot,WarehouseNameSnapshot,SkuSnapshot,ProductNameSnapshot,UnitSnapshot)
                VALUES(NEWID(),1,0,SYSDATETIME(),N'隔离测试',@Sequence,@WarehouseId,@ProductId,10,@Quantity,@Quantity,
                       N'InventoryQueryTest',NEWID(),NEWID(),@DocumentNo,NEWID(),NEWID(),SYSDATETIME(),
                       N'WH-QUERY',N'库存查询仓',@Sku,N'库存查询产品',N'PCS');
                """, new
            {
                BalanceId = BalanceIds[index], WarehouseId, ProductId = productId, sample.Sku,
                sample.DocumentNo, Quantity = index + 1m, Sequence = index + 1L
            });
        }

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = ConnectionString(databaseName)
        }).Build());
        services.AddSingleton<ICurrentUser>(new FixtureUser());
        SwiftExport.Infrastructure.DependencyInjection.AddInfrastructureServices(services);
        provider = services.BuildServiceProvider();
    }

    /// <summary>在独立作用域中调用真实库存仓储，确保 count 和 page SQL 均由 SQL Server 执行。</summary>
    public async Task<T> QueryAsync<T>(Func<IInventoryRepository, Task<T>> action)
    {
        using var scope = provider!.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<IInventoryRepository>());
    }

    /// <summary>只向本夹具拥有的随机数据库执行参数化建表或造数 SQL。</summary>
    public async Task<int> ExecuteAsync(string sql, object? parameters = null)
    {
        RequireOwnedDatabase();
        await using var connection = new SqlConnection(ConnectionString(databaseName));
        await connection.OpenAsync();
        return await connection.ExecuteAsync(sql, parameters);
    }

    /// <summary>释放服务后删除且确认随机隔离库，不接受其他数据库名称。</summary>
    public async ValueTask DisposeAsync()
    {
        if (provider is not null) await provider.DisposeAsync();
        if (!created) return;
        RequireOwnedDatabase();
        SqlConnection.ClearAllPools();
        await using var master = new SqlConnection(ConnectionString("master"));
        await master.OpenAsync();
        await master.ExecuteAsync($"ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{databaseName}];");
        if (await master.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM sys.databases WHERE name=@databaseName;", new { databaseName }) != 0)
            throw new InvalidOperationException("库存查询隔离测试库清理未完成。");
        created = false;
    }

    private void RequireOwnedDatabase()
    {
        if (!databaseName.StartsWith(DatabasePrefix, StringComparison.Ordinal)
            || !Guid.TryParseExact(databaseName[DatabasePrefix.Length..], "N", out _))
            throw new InvalidOperationException("拒绝操作非本实例库存查询测试库。");
    }

    private sealed record Sample(string Sku, string DocumentNo);

    private sealed class FixtureUser : ICurrentUser
    {
        public string UserName => "库存查询SQL测试";
        public Guid? UserId => null;
        public bool IsAuthenticated => true;
    }
}
