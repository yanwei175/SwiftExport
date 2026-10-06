using Dapper;
using Npgsql;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Tests.Database;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SwiftExport.Application.Interfaces.MasterData;
using SwiftExport.Core.Interfaces.Auth;

namespace SwiftExport.Application.Tests.MasterData;

/// <summary>复用获批单PG测试库及原场景数据；释放连接、完整导出后仅重置该新库public。</summary>
internal sealed class ProcurementMasterDataSqlFixture : IAsyncDisposable
{
    private PostgreSqlLegacyTestDatabase? database;
    private ServiceProvider? provider;
    public Guid UserId { get; } = Guid.NewGuid();


    /// <summary>在获批固定PG基线插入原夹具数据，通过真实DI调用生产服务和仓储。</summary>
    public async Task InitializeAsync(TestContext context)
    {
        database = await PostgreSqlLegacyTestDatabase.AcquireAsync(context);
        await ExecuteAsync("INSERT INTO public.sys_dict_data(uid,dict_code,data_name_en,is_enabled,is_deleted,is_system) VALUES(gen_random_uuid(),'Unit','EA',TRUE,FALSE,FALSE);");
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

    /// <summary>以新请求 scope 调用生产服务，验证服务和仓储共享同一会话。</summary>
    public async Task<T> QueryAsync<T>(Func<IProcurementMasterDataQueryService, Task<T>> action)
    {
        await using var scope = provider!.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<IProcurementMasterDataQueryService>());
    }

    /// <summary>只操作当前实例的临时库，参数化准备数据或检查结果。</summary>
    public async Task<int> ExecuteAsync(string sql, object? parameters = null)
    {
        await using var connection = new NpgsqlConnection(database!.ConnectionString);
        await connection.OpenAsync();
        return await connection.ExecuteAsync(sql, parameters);
    }

    /// <summary>建立产品资格测试数据，不为无效产品伪造字典或供应商目录关系。</summary>
    public async Task<Guid> AddProductAsync(string sku, string? unit = " ea ", string supply = "采购",
        string material = "成品", bool active = true, bool deleted = false)
    {
        var uid = Guid.NewGuid();
        await ExecuteAsync("""
            INSERT INTO public.product(uid,sku,description_zh,description_en,main_image,link_sku,features_zh,color_id,unit,supply_type,material_type,is_active,is_deleted,bom_version)
            VALUES(@uid,@sku,'产品描述','Product description','/images/main.png','LINK-1','配置',NULL,@unit,@supply,@material,@active,@deleted,0);
            """, new { uid, sku, unit, supply, material, active, deleted });
        return uid;
    }

    /// <summary>复用获批单PG测试库及原场景数据；释放连接、完整导出后仅重置该新库public。</summary>
    public async ValueTask DisposeAsync()
    {
        if (provider is not null) await provider.DisposeAsync();
        if (database is not null) await database.DisposeAsync();
    }

    /// <summary>仅提供已认证测试身份；候选查询不依赖数据库用户或角色表。</summary>
    private sealed class FixtureUser(Guid uid) : ICurrentUser
    {
        public string UserName => "主数据候选测试";
        public Guid? UserId => uid;
        public bool IsAuthenticated => true;
    }
}
