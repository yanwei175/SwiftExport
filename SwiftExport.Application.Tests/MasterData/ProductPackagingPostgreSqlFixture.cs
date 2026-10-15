using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Services.Products.Catalog.Maintenance;
using SwiftExport.Application.Services.Products.Catalog.Query;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Core.Entities.DemandPlanning;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Inventory.Warehouses;
using SwiftExport.Core.Interfaces.Products.Attributes;
using SwiftExport.Core.Interfaces.Products.Catalog;
using SwiftExport.Infrastructure;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Application.Tests.MasterData;

/// <summary>包装验收专用真实 PG 夹具，仅允许明确授权的既有隔离库，不迁移/建库/清库；只清理记录过的本次物料身份。</summary>
internal sealed class ProductPackagingPostgreSqlFixture : IAsyncDisposable
{
    internal const string Database = "dv_next_product_main_image_tests_20261015";
    internal readonly string ConnectionString;
    private readonly ServiceProvider _provider;
    private readonly List<Scope> _scopes = [];
    private readonly HashSet<Guid> _products = [];
    private readonly HashSet<Guid> _sources = [];
    internal readonly string Prefix = "PKG_TEST_" + Guid.NewGuid().ToString("N");
    private int _sequence;

    private ProductPackagingPostgreSqlFixture(string connection)
    {
        ConnectionString = connection;
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Default"] = connection }).Build());
        services.AddLogging(); services.AddSingleton<ICurrentUser, TestUser>(); services.AddInfrastructureServices();
        services.AddScoped<ServiceBaseContext>(); services.AddScoped<ProductMaintenanceService>();
        _provider = services.BuildServiceProvider();
    }
    /// <summary>显式开关不是数据库授权替代物；执行者获准后设置，凭据仅从环境读取并严格核对真实服务端。</summary>
    internal static async Task<ProductPackagingPostgreSqlFixture> CreateAsync()
    {
        var value = Environment.GetEnvironmentVariable("SWIFTEXPORT_PG_PACKAGING_TEST_CONNECTION");
        if (Environment.GetEnvironmentVariable("SWIFTEXPORT_RUN_PG_PACKAGING_TESTS") != "1" || string.IsNullOrWhiteSpace(value))
            Assert.Inconclusive("未显式启用本任务获准的包装隔离库测试。");
        var settings = new NpgsqlConnectionStringBuilder(value);
        Assert.AreEqual("192.168.3.12", settings.Host); Assert.AreEqual(5432, settings.Port); Assert.AreEqual(Database, settings.Database);
        Assert.IsTrue(string.IsNullOrEmpty(settings.SearchPath) || settings.SearchPath == "public");
        settings.CommandTimeout = 15; settings.ApplicationName = "SwiftExport.Packaging.Tests";
        var fixture = new ProductPackagingPostgreSqlFixture(settings.ConnectionString);
        try
        {
            using var scope = fixture.NewScope();
            Assert.AreEqual(Database, await scope.Session.Connection.ExecuteScalarAsync<string>("SELECT current_database()"));
            Assert.AreEqual("192.168.3.12", await scope.Session.Connection.ExecuteScalarAsync<string>("SELECT host(inet_server_addr())"));
            Assert.AreEqual("5432", await scope.Session.Connection.ExecuteScalarAsync<string>("SELECT current_setting('port')"));
            Assert.IsTrue(await scope.Session.Connection.ExecuteScalarAsync<bool>("""
                SELECT EXISTS(SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='product_package' AND column_name='owner_product_id')
                  AND EXISTS(SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='product_package' AND column_name='packaging_material_id')
                  AND NOT EXISTS(SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='product_package' AND column_name='is_default')
                """), "隔离库须先经独立授权完成现有迁移；测试不会自动调整结构。");
            Console.WriteLine($"包装 PG 夹具前缀：{fixture.Prefix}");
            return fixture;
        }
        catch { await fixture.DisposeAsync(); throw; }
    }
    /// <summary>每个 scope 使用独立物理会话，真实并发不共享连接或事务。</summary>
    internal Scope NewScope()
    {
        var scope = new Scope(_provider.CreateScope()); _scopes.Add(scope); return scope;
    }
    /// <summary>只新增随机编号的本次物料；可经真实保存用例建包材，随后记录返回身份。</summary>
    internal async Task<Product> AddProductAsync(bool packaging = false)
    {
        using var scope = NewScope();
        var sku = Prefix + "_" + Interlocked.Increment(ref _sequence);
        var result = await scope.Maintenance.AddAsync(new() { Data = new ProductDto { Sku = sku,
            MaterialType = packaging ? MaterialTypesEnum.包材 : MaterialTypesEnum.成品, SupplyType = SupplyTypeEnums.采购, Unit = "Pcs" } });
        // 即使应用返回未知结果，也只读按本次唯一编号找回身份，以免测试退出留下无法追踪的夹具。
        var row = await scope.Repository.GetBySkuAsync(sku);
        if (row is not null) _products.Add(row.Uid);
        Assert.IsTrue(result.Success, result.Message); Assert.IsNotNull(row); return row;
    }
    /// <summary>最小来源夹具只为比较箱单包装投影，不创建或过账业务单据。</summary>
    internal async Task<GrossDemand> AddSourceAsync(Guid owner, bool nonStandard = false, string sourceType = "SO")
    {
        using var scope = NewScope();
        var source = new GrossDemand { ProductId = nonStandard ? null : owner, IsNonStandard = nonStandard,
            SourceType = sourceType, SourceUid = Guid.NewGuid(), SourceLineId = Guid.NewGuid(), SourceNo = Prefix,
            SkuSnapshot = Prefix, DemandQty = 24, UnitSnapshot = "Pcs" };
        scope.Services.GetRequiredService<RepositoryBaseContext>().AuditStamp.StampForInsert(source);
        source.Version = 1; _sources.Add(source.Uid);
        // 夹具使用已知 Uid 在发送 INSERT 前登记；不调用会重新生成身份的泛型 Add，以免异常后丢失清理范围。
        var metadata = EntityMetadataCache.GetMetadata<GrossDemand>();
        var columns = string.Join(',', metadata.Columns.Select(column => '"' + column.ColumnName + '"'));
        var values = string.Join(',', metadata.Columns.Select(column => '@' + column.ParameterName));
        var count = await scope.Session.Connection.ExecuteAsync($"INSERT INTO public.gross_demand ({columns}) VALUES ({values})", PostgreSqlParameters.Entity(source, metadata));
        Assert.AreEqual(1, count); return source;
    }
    /// <summary>只按已记录物料/来源 Uid 清理本次关系和主数据，不处理任何原有业务记录。</summary>
    public async ValueTask DisposeAsync()
    {
        foreach (var scope in _scopes) scope.Dispose();
        if (_products.Count > 0 || _sources.Count > 0)
        {
            await using var connection = new NpgsqlConnection(ConnectionString); await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            var parameters = new PostgreSqlParameters(); parameters.AddIds("Products", _products.ToArray()); parameters.AddIds("Sources", _sources.ToArray());
            await connection.ExecuteAsync("DELETE FROM public.product_package WHERE owner_product_id=ANY(@Products)", parameters, transaction);
            await connection.ExecuteAsync("DELETE FROM public.gross_demand WHERE uid=ANY(@Sources)", parameters, transaction);
            await connection.ExecuteAsync("DELETE FROM public.product WHERE uid=ANY(@Products)", parameters, transaction);
            await transaction.CommitAsync();
            Assert.AreEqual(0, await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.product_package WHERE owner_product_id=ANY(@Products)", parameters));
            Assert.AreEqual(0, await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.product WHERE uid=ANY(@Products)", parameters));
            Assert.AreEqual(0, await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.gross_demand WHERE uid=ANY(@Sources)", parameters));
            Console.WriteLine($"包装 PG 清理完成：{Prefix}，物料 {_products.Count}、来源 {_sources.Count}，按身份复核残留为 0。");
        }
        await _provider.DisposeAsync();
    }
    /// <summary>真实 Application 与 Repository 的同一请求上下文；不经过 HTTP，因此不冒称真实 API 人工验收。</summary>
    internal sealed class Scope : IDisposable
    {
        private readonly IServiceScope _scope;
        private bool _disposed;
        internal IServiceProvider Services => _scope.ServiceProvider;
        internal DbSession Session => Services.GetRequiredService<DbSession>();
        internal IProductRepository Repository => Services.GetRequiredService<IProductRepository>();
        internal ProductMaintenanceService Maintenance => Services.GetRequiredService<ProductMaintenanceService>();
        internal ProductQueryService Query => new(Repository, Services.GetRequiredService<IWarehouseQueryRepository>(),
            Services.GetRequiredService<ICurrentUser>(), null!, Services.GetRequiredService<ServiceBaseContext>(), Services.GetRequiredService<IProductAttributeRepository>());
        internal Scope(IServiceScope scope) => _scope = scope;
        public void Dispose() { if (_disposed) return; _disposed = true; _scope.Dispose(); }
    }
    /// <summary>仅提供本次夹具审计身份，不创建数据库账号、角色或权限。</summary>
    private sealed class TestUser : ICurrentUser
    {
        public string UserName => "__pg_packaging_acceptance__";
        public Guid? UserId => null;
        public bool IsAuthenticated => true;
    }
}
