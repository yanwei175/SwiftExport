using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SwiftExport.Application.Interfaces.MasterData;
using SwiftExport.Core.Interfaces.Auth;

namespace SwiftExport.Application.Tests.MasterData;

/// <summary>为候选查询创建唯一 LocalDB 临时库；只建测试表，不读取业务配置、不执行部署迁移。</summary>
internal sealed class ProcurementMasterDataSqlFixture : IAsyncDisposable
{
    private readonly string databaseName = "SwiftExport_MasterDataLookupTests_" + Guid.NewGuid().ToString("N");
    private ServiceProvider? provider;
    private bool created;
    public Guid UserId { get; } = Guid.NewGuid();

    /// <summary>仅允许本机 LocalDB 集成认证；不连接既有业务库。</summary>
    private static string ConnectionString(string database) => new SqlConnectionStringBuilder
    {
        DataSource = @"(LocalDB)\MSSQLLocalDB", InitialCatalog = database, IntegratedSecurity = true,
        TrustServerCertificate = true, ConnectTimeout = 15
    }.ConnectionString;

    /// <summary>初始化本实例拥有的测试库及最小列结构，通过真实 DI 注册调用生产服务和仓储。</summary>
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
            CREATE TABLE dbo.Customer(Uid uniqueidentifier PRIMARY KEY,CustomerCode nvarchar(100),CustomerName nvarchar(300),IsActive bit NOT NULL,IsDeleted bit);
            CREATE TABLE dbo.Supplier(Uid uniqueidentifier PRIMARY KEY,SupplierCode nvarchar(100),SupplierName nvarchar(300),IsActive bit NOT NULL,IsDeleted bit);
            CREATE TABLE dbo.ProductColor(Uid uniqueidentifier PRIMARY KEY,ColorNameZh nvarchar(300),IsDeleted bit);
            CREATE TABLE dbo.Product(Uid uniqueidentifier PRIMARY KEY,Sku nvarchar(200),DescriptionZh nvarchar(300),DescriptionEn nvarchar(300),
              MainImage nvarchar(1000),LinkSku nvarchar(200),FeaturesZh nvarchar(300),ColorId uniqueidentifier,
              Unit nvarchar(100),SupplyType nvarchar(100),MaterialType nvarchar(100),IsActive bit NOT NULL,IsDeleted bit);
            CREATE TABLE dbo.CustomerProduct(Uid uniqueidentifier PRIMARY KEY,CustomerId uniqueidentifier,SkuId uniqueidentifier,CustomerSku nvarchar(200),IsActive bit,IsDeleted bit);
            CREATE TABLE dbo.SupplierProduct(Uid uniqueidentifier PRIMARY KEY,SupplierId uniqueidentifier,SkuId uniqueidentifier,SupplierSku nvarchar(200),UnitPrice decimal(18,6),TaxPrice decimal(18,6),IsDeleted bit);
            CREATE TABLE dbo.SysDictData(Uid uniqueidentifier PRIMARY KEY,DictCode nvarchar(100),DataNameEn nvarchar(100),IsEnabled bit,IsDeleted bit);
            INSERT dbo.SysDictData VALUES(NEWID(),N'Unit',N'EA',1,0);
            """);
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = ConnectionString(databaseName)
        }).Build());
        services.AddSingleton<ICurrentUser>(new FixtureUser(UserId));
        SwiftExport.Infrastructure.DependencyInjection.AddInfrastructureServices(services);
        SwiftExport.Application.DependencyInjection.AddApplicationServices(services);
        provider = services.BuildServiceProvider();
    }

    /// <summary>以新请求 scope 调用生产服务，验证服务和仓储共享同一会话。</summary>
    public async Task<T> QueryAsync<T>(Func<IProcurementMasterDataQueryService, Task<T>> action)
    {
        using var scope = provider!.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<IProcurementMasterDataQueryService>());
    }

    /// <summary>只操作当前实例的临时库，参数化准备数据或检查结果。</summary>
    public async Task<int> ExecuteAsync(string sql, object? parameters = null)
    {
        RequireOwnedDatabase();
        await using var connection = new SqlConnection(ConnectionString(databaseName));
        await connection.OpenAsync();
        return await connection.ExecuteAsync(sql, parameters);
    }

    /// <summary>建立产品资格测试数据，不为无效产品伪造字典或供应商目录关系。</summary>
    public async Task<Guid> AddProductAsync(string sku, string? unit = " ea ", string supply = "采购",
        string material = "成品", bool active = true, bool deleted = false)
    {
        var uid = Guid.NewGuid();
        await ExecuteAsync("""
            INSERT dbo.Product(Uid,Sku,DescriptionZh,DescriptionEn,MainImage,LinkSku,FeaturesZh,ColorId,Unit,SupplyType,MaterialType,IsActive,IsDeleted)
            VALUES(@uid,@sku,N'产品描述',N'Product description',N'/images/main.png',N'LINK-1',N'配置',NULL,@unit,@supply,@material,@active,@deleted);
            """, new { uid, sku, unit, supply, material, active, deleted });
        return uid;
    }

    /// <summary>只释放并删除本实例创建的随机测试库，失败也不转向任何其他库。</summary>
    public async ValueTask DisposeAsync()
    {
        if (provider is not null) await provider.DisposeAsync();
        if (!created) return;
        RequireOwnedDatabase();
        await using var master = new SqlConnection(ConnectionString("master"));
        await master.OpenAsync();
        await master.ExecuteAsync($"ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{databaseName}];");
        if (await master.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM sys.databases WHERE name=@databaseName;", new { databaseName }) != 0)
            throw new InvalidOperationException("主数据候选测试库清理未完成。");
        created = false;
    }

    /// <summary>破坏性清理前校验固定前缀和完整 GUID，名称只能由本实例构造。</summary>
    private void RequireOwnedDatabase()
    {
        const string prefix = "SwiftExport_MasterDataLookupTests_";
        if (!databaseName.StartsWith(prefix, StringComparison.Ordinal)
            || !Guid.TryParseExact(databaseName[prefix.Length..], "N", out _))
            throw new InvalidOperationException("拒绝操作非本实例测试库。");
    }

    /// <summary>仅提供已认证测试身份；候选查询不依赖数据库用户或角色表。</summary>
    private sealed class FixtureUser(Guid uid) : ICurrentUser
    {
        public string UserName => "主数据候选测试";
        public Guid? UserId => uid;
        public bool IsAuthenticated => true;
    }
}
