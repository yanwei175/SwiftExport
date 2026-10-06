using Dapper;
using Npgsql;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Tests.Database;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SwiftExport.Application.Interfaces.Inventory;
using SwiftExport.Application.Interfaces.Inventory.Warehouses;
using SwiftExport.Core.Interfaces.Auth;

namespace SwiftExport.Application.Tests.Inventory;

/// <summary>复用获批单PG测试库及原场景数据；释放连接、完整导出后仅重置该新库public。</summary>
internal sealed class WarehouseCandidateSqlFixture : IAsyncDisposable
{
    private PostgreSqlLegacyTestDatabase? database;
    private ServiceProvider? provider;
    public Guid UserId { get; } = Guid.NewGuid();
    public Guid RoleId { get; } = Guid.NewGuid();


    /// <summary>在固定PG基线写入原测试账号和应用权限，使用真实生产DI；不写入业务单据。</summary>
    public async Task InitializeAsync(TestContext context)
    {
        database = await PostgreSqlLegacyTestDatabase.AcquireAsync(context);
        await ExecuteAsync("""
            INSERT INTO public.sys_user(uid,user_name,password_hash,credential_version,is_active) VALUES(@UserId,'warehouse-test','test-only',1,TRUE);
            INSERT INTO public.sys_role(uid,role_code,role_name,is_active,is_system) VALUES(@RoleId,'WAREHOUSE_CANDIDATE','仓库候选',TRUE,FALSE);
            INSERT INTO public.sys_user_role(uid,user_id,role_id) VALUES(gen_random_uuid(),@UserId,@RoleId);
            INSERT INTO public.sys_permission(uid,permission_key,display_name,group_key,sort_order,is_active)
            VALUES(gen_random_uuid(),'Procurement.Order.Create','采购创建','Procurement',1,TRUE),
                  (gen_random_uuid(),'Inventory.Warehouse.View','仓库查看','Inventory',2,TRUE);
            INSERT INTO public.sys_role_permission(uid,role_id,permission_id) SELECT gen_random_uuid(),@RoleId,uid FROM public.sys_permission;
            """, new { UserId, RoleId });
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = database!.ConnectionString
        }).Build());
        services.AddSingleton<ICurrentUser>(new FixtureUser(UserId));
        SwiftExport.Infrastructure.DependencyInjection.AddInfrastructureServices(services);
        SwiftExport.Application.DependencyInjection.AddApplicationServices(services);
        provider = services.BuildServiceProvider();
    }

    /// <summary>在新作用域调用真实候选服务，验证服务和仓储共享会话及事务。</summary>
    public async Task<T> QueryAsync<T>(Func<IWarehouseCandidateQueryService, Task<T>> action)
    {
        await using var scope = provider!.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<IWarehouseCandidateQueryService>());
    }

    /// <summary>在新作用域调用真实库存服务，供仓库选择器验证读权限与生产 SQL。</summary>
    public async Task<T> QueryInventoryAsync<T>(Func<IWarehouseQueryService, Task<T>> action)
    {
        await using var scope = provider!.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<IWarehouseQueryService>());
    }

    /// <summary>仅向当前夹具数据库执行参数化准备或断言 SQL。</summary>
    public async Task<int> ExecuteAsync(string sql, object? parameters = null)
    {
        await using var connection = new NpgsqlConnection(database!.ConnectionString);
        await connection.OpenAsync();
        return await connection.ExecuteAsync(sql, parameters);
    }

    /// <summary>复用获批单PG测试库及原场景数据；释放连接、完整导出后仅重置该新库public。</summary>
    public async ValueTask DisposeAsync()
    {
        if (provider is not null) await provider.DisposeAsync();
        if (database is not null) await database.DisposeAsync();
    }

    private sealed class FixtureUser(Guid uid) : ICurrentUser
    {
        public string UserName => "仓库候选SQL测试";
        public Guid? UserId => uid;
        public bool IsAuthenticated => true;
    }
}
