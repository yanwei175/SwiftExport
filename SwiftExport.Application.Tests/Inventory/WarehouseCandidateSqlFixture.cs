using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SwiftExport.Application.Interfaces.Inventory;
using SwiftExport.Core.Interfaces.Auth;

namespace SwiftExport.Application.Tests.Inventory;

/// <summary>仓库候选隔离 SQL 夹具；只操作随机临时库，不读取应用配置或执行迁移。</summary>
internal sealed class WarehouseCandidateSqlFixture : IAsyncDisposable
{
    private readonly string databaseName = "SwiftExport_WarehouseCandidateTests_" + Guid.NewGuid().ToString("N");
    private ServiceProvider? provider;
    private bool created;
    public Guid UserId { get; } = Guid.NewGuid();
    public Guid RoleId { get; } = Guid.NewGuid();

    /// <summary>固定使用本机 LocalDB 集成认证，库名只接受本夹具生成值。</summary>
    private static string ConnectionString(string database) => new SqlConnectionStringBuilder
    {
        DataSource = @"(LocalDB)\MSSQLLocalDB", InitialCatalog = database, IntegratedSecurity = true,
        TrustServerCertificate = true, ConnectTimeout = 15
    }.ConnectionString;

    /// <summary>创建最小表、测试账号及真实生产 DI；不创建库存余额、采购、BOM 或业务单据表。</summary>
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
            CREATE TABLE dbo.Supplier(Uid uniqueidentifier PRIMARY KEY,SupplierCode nvarchar(100),SupplierName nvarchar(300),IsActive bit NOT NULL,IsDeleted bit);
            CREATE TABLE dbo.WarehouseSite(Uid uniqueidentifier PRIMARY KEY,Version int NOT NULL,Code nvarchar(100),Name nvarchar(300),
              SiteType int NOT NULL,SupplierId uniqueidentifier NULL,SupplierCodeSnapshot nvarchar(100) NULL,SupplierNameSnapshot nvarchar(300) NULL,
              IsActive bit NOT NULL,IsDeleted bit);
            CREATE TABLE dbo.SysUser(Uid uniqueidentifier PRIMARY KEY,IsActive bit NOT NULL,IsDeleted bit);
            CREATE TABLE dbo.SysRoles(Uid uniqueidentifier PRIMARY KEY,RoleName nvarchar(100),IsDeleted bit);
            CREATE TABLE dbo.SysUser_Roles(Uid uniqueidentifier PRIMARY KEY,UserId uniqueidentifier,RoleId uniqueidentifier,IsDeleted bit);
            CREATE TABLE dbo.SysUser_ModulePermissions(Uid uniqueidentifier PRIMARY KEY,Version int NOT NULL DEFAULT(1),IsDeleted bit NOT NULL DEFAULT(0),
              CreatedDate datetime2 NULL,CreatedBy nvarchar(256) NULL,UpdatedDate datetime2 NULL,UpdatedBy nvarchar(256) NULL,
              UserId uniqueidentifier NOT NULL,PermissionKey nvarchar(128) NOT NULL);
            INSERT dbo.SysUser VALUES(@UserId,1,0);
            INSERT dbo.SysRoles VALUES(@RoleId,N'Procurement.Order.Create',0);
            INSERT dbo.SysUser_Roles VALUES(NEWID(),@UserId,@RoleId,0);
            INSERT dbo.SysUser_ModulePermissions(Uid,UserId,PermissionKey) VALUES(NEWID(),@UserId,N'Module.Inventory.Access');
            """, new { UserId, RoleId });
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

    /// <summary>在新作用域调用真实候选服务，验证服务和仓储共享会话及事务。</summary>
    public async Task<T> QueryAsync<T>(Func<IWarehouseCandidateQueryService, Task<T>> action)
    {
        using var scope = provider!.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<IWarehouseCandidateQueryService>());
    }

    /// <summary>在新作用域调用真实库存服务，供仓库选择器验证读权限与生产 SQL。</summary>
    public async Task<T> QueryInventoryAsync<T>(Func<IInventoryService, Task<T>> action)
    {
        using var scope = provider!.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<IInventoryService>());
    }

    /// <summary>仅向当前夹具数据库执行参数化准备或断言 SQL。</summary>
    public async Task<int> ExecuteAsync(string sql, object? parameters = null)
    {
        RequireOwnedDatabase();
        await using var connection = new SqlConnection(ConnectionString(databaseName));
        await connection.OpenAsync();
        return await connection.ExecuteAsync(sql, parameters);
    }

    /// <summary>释放 DI 后删除且确认本实例临时库不存在；拒绝任何非本夹具名称。</summary>
    public async ValueTask DisposeAsync()
    {
        if (provider is not null) await provider.DisposeAsync();
        if (!created) return;
        RequireOwnedDatabase();
        await using var master = new SqlConnection(ConnectionString("master"));
        await master.OpenAsync();
        await master.ExecuteAsync($"ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{databaseName}];");
        if (await master.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM sys.databases WHERE name=@databaseName;", new { databaseName }) != 0)
            throw new InvalidOperationException("仓库候选测试库清理未完成。");
        created = false;
    }

    private void RequireOwnedDatabase()
    {
        const string prefix = "SwiftExport_WarehouseCandidateTests_";
        if (!databaseName.StartsWith(prefix, StringComparison.Ordinal)
            || !Guid.TryParseExact(databaseName[prefix.Length..], "N", out _))
            throw new InvalidOperationException("拒绝操作非本实例测试库。");
    }

    private sealed class FixtureUser(Guid uid) : ICurrentUser
    {
        public string UserName => "仓库候选SQL测试";
        public Guid? UserId => uid;
        public bool IsAuthenticated => true;
    }
}
