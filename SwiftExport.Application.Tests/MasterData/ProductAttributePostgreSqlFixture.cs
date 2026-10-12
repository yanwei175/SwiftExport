using Dapper;
using Mapster;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.Auth.Permissions.Evaluation;
using SwiftExport.Application.Models.Auth.Permissions.Evaluation;
using SwiftExport.Application.Services.Products.Attributes;
using SwiftExport.Application.Services.Products.Categories;
using SwiftExport.Application.Services.Products.ProductMasters.Import;
using SwiftExport.Application.Services.Products.ProductMasters.Maintenance;
using SwiftExport.Application.Services.Products.ProductMasters.Query;
using SwiftExport.Contracts.DTOs.Products.Attributes;
using SwiftExport.Contracts.DTOs.Products.ProductMasters.Requests;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Products.Attributes;
using SwiftExport.Core.Interfaces.Products.Catalog;
using SwiftExport.Core.Interfaces.Products.Categories;
using SwiftExport.Core.Interfaces.Products.Colors;
using SwiftExport.Core.Interfaces.Products.ProductMasters;
using SwiftExport.Core.Interfaces.Products.ProductNeutrals;
using SwiftExport.Core.Interfaces.Suppliers.Management;
using SwiftExport.Infrastructure;
using SwiftExport.Infrastructure.Data;

namespace SwiftExport.Application.Tests.MasterData;

/// <summary>仅连接本任务批准的复制库；创建独立 Uid 夹具，退出时只清理夹具所属记录。</summary>
internal sealed class ProductAttributePostgreSqlFixture : IAsyncDisposable
{
    internal readonly string ConnectionString;
    internal readonly ServiceProvider Provider;
    internal readonly TestUser User = new();
    internal readonly List<Guid> CategoryIds = [];
    internal readonly List<Guid> MasterIds = [];
    internal readonly List<Guid> ColorIds = [];
    internal readonly List<Scope> Scopes = [];
    internal ProductCategory Category = null!, OtherCategory = null!;
    internal ProductMaster Master = null!;

    private ProductAttributePostgreSqlFixture(string connection)
    {
        ConnectionString = connection;
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["ConnectionStrings:Default"] = connection }).Build());
        services.AddSingleton<ICurrentUser>(User);
        services.AddInfrastructureServices();
        Provider = services.BuildServiceProvider();
    }

    /// <summary>严格核对实例和库名；未配置时明确标记未运行，绝不回退使用 dv_next。</summary>
    internal static async Task<ProductAttributePostgreSqlFixture> CreateAsync()
    {
        var value = Environment.GetEnvironmentVariable("SWIFTEXPORT_PG_ATTRIBUTE_TEST_CONNECTION");
        if (Environment.GetEnvironmentVariable("SWIFTEXPORT_RUN_PG_ATTRIBUTE_TESTS") != "1" || string.IsNullOrWhiteSpace(value))
            Assert.Inconclusive("未显式启用产品标签专用复制库测试。");
        var settings = new NpgsqlConnectionStringBuilder(value);
        Assert.AreEqual("192.168.3.12", settings.Host);
        Assert.AreEqual(5432, settings.Port);
        Assert.AreEqual("dv_next_product_attribute_tests_20261012", settings.Database);
        Assert.IsTrue(string.IsNullOrEmpty(settings.SearchPath) || settings.SearchPath == "public");
        var fixture = new ProductAttributePostgreSqlFixture(value!);
        try
        {
            var scope = fixture.NewScope();
            fixture.Category = await fixture.AddCategoryAsync(scope, "顶喷");
            fixture.OtherCategory = await fixture.AddCategoryAsync(scope, "另一分类");
            fixture.Master = new ProductMaster { MasterSku = "TAG-" + Guid.NewGuid().ToString("N"), CategoryId = fixture.Category.Uid,
                ProductType = "保留旧类型", ProductTag = "保留旧标签" };
            var write = await scope.Services.GetRequiredService<IProductMasterRepository>().AddAsync(fixture.Master);
            Assert.IsTrue(write.Success);
            fixture.MasterIds.Add(write.Uid);
            return fixture;
        }
        catch { await fixture.DisposeAsync(); throw; }
    }

    /// <summary>每条并发用例使用独立 request scope，仓储与服务在各自 scope 内共享会话。</summary>
    internal Scope NewScope()
    {
        var scope = new Scope(Provider.CreateScope(), User);
        Scopes.Add(scope);
        return scope;
    }

    /// <summary>记录仅属于本测试的分类身份，名称唯一以免依赖复制数据。</summary>
    private async Task<ProductCategory> AddCategoryAsync(Scope scope, string name)
    {
        var category = new ProductCategory { CategoryName = name + Guid.NewGuid().ToString("N"), CategoryCode = Guid.NewGuid().ToString("N") };
        var write = await scope.Services.GetRequiredService<IProductCategoryRepository>().AddAsync(category);
        Assert.IsTrue(write.Success);
        CategoryIds.Add(write.Uid);
        return category;
    }

    /// <summary>用真实定义用例同批创建组及两个选项，返回服务端回执快照。</summary>
    internal async Task<ProductAttributeGroupDto> DefineAsync(Scope scope, Guid? categoryId = null,
        ProductAttributeSelectionMode mode = ProductAttributeSelectionMode.Multiple)
    {
        var temporaryId = Guid.NewGuid();
        var request = new SaveProductAttributeDefinitionsRequest();
        request.GroupChanges.CreateItems.Add(new() { RowIndex = 1, Data = new() { ClientUid = temporaryId, GroupCode = "INSTALL", GroupName = "安装方式", SelectionMode = mode } });
        request.OptionChanges.CreateItems.Add(new() { RowIndex = 1, Data = new() { AttributeGroupId = temporaryId, OptionCode = "CEILING", OptionName = "吸顶" } });
        request.OptionChanges.CreateItems.Add(new() { RowIndex = 2, Data = new() { AttributeGroupId = temporaryId, OptionCode = "WALL", OptionName = "挂墙", SortOrder = 1 } });
        var result = await scope.Definitions.BatchSaveAsync(categoryId ?? Category.Uid, request);
        Assert.IsTrue(result.Success, result.Message);
        Assert.AreEqual(1, result.Data.GroupResults.SuccessCount);
        Assert.AreEqual(2, result.Data.OptionResults.SuccessCount);
        var group = result.Data.Definitions.Groups.Single();
        Assert.AreNotEqual(temporaryId, group.Uid);
        Assert.IsTrue(group.Options.All(option => option.AttributeGroupId == group.Uid));
        return group;
    }

    /// <summary>从服务器当前头部复制详情请求；默认不改变旧标签。</summary>
    internal async Task<SaveProductMasterDetailRequest> DetailRequestAsync(Scope scope)
    {
        var detail = await scope.Query.GetDetailAsync(Master.Uid);
        Assert.IsTrue(detail.Success, detail.Message);
        return new() { ProductMasterId = Master.Uid, ExpectedVersion = detail.Data.Version, Master = detail.Data.Master.Adapt<SwiftExport.Contracts.DTOs.Products.ProductMasters.ProductMasterWriteDto>() };
    }

    /// <summary>只移除捕获的测试分类、主型号及其自建标签；保留复制库的其他数据和结构。</summary>
    public async ValueTask DisposeAsync()
    {
        foreach (var scope in Scopes) scope.Dispose();
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await connection.ExecuteAsync("""
            DELETE FROM public.product_master_attribute_selection WHERE product_master_id=ANY(@Masters)
               OR option_id IN (SELECT o.uid FROM public.product_attribute_option o JOIN public.product_attribute_group g ON g.uid=o.attribute_group_id WHERE g.category_id=ANY(@Categories));
            DELETE FROM public.product_attribute_option WHERE attribute_group_id IN (SELECT uid FROM public.product_attribute_group WHERE category_id=ANY(@Categories));
            DELETE FROM public.product_attribute_group WHERE category_id=ANY(@Categories);
            DELETE FROM public.product_neutral WHERE product_master_id=ANY(@Masters);
            DELETE FROM public.product_master WHERE uid=ANY(@Masters);
            DELETE FROM public.product_color WHERE uid=ANY(@Colors);
            DELETE FROM public.product_category WHERE uid=ANY(@Categories);
            """, new { Masters = MasterIds.ToArray(), Categories = CategoryIds.ToArray(), Colors = ColorIds.ToArray() }, transaction);
        await transaction.CommitAsync();
        await Provider.DisposeAsync();
    }

    /// <summary>为本次真实服务测试提供可控身份和权限投影，不读写生产账号。</summary>
    internal sealed class TestUser : ICurrentUser, IPermissionEvaluationService
    {
        internal bool Admin = true, Active = true;
        internal IReadOnlyList<string> Keys = [];
        /// <inheritdoc />
        public string UserName => "product-attribute-tests";
        /// <inheritdoc />
        public Guid? UserId { get; } = Guid.NewGuid();
        /// <inheritdoc />
        public bool IsAuthenticated => Active;
        /// <inheritdoc />
        public Task<UserPermissionProjection?> GetProjectionAsync(Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Active ? new UserPermissionProjection { IsAdmin = Admin, Permissions = Keys } : null);
        /// <inheritdoc />
        public Task<bool> HasPermissionAsync(Guid userId, string permissionKey, CancellationToken cancellationToken = default) => Task.FromResult(Active && (Admin || Keys.Contains(permissionKey, StringComparer.Ordinal)));
    }

    /// <summary>独立应用会话，不把其他请求的事务传给本请求。</summary>
    internal sealed class Scope : IDisposable
    {
        private readonly IServiceScope _scope;
        internal IServiceProvider Services => _scope.ServiceProvider;
        internal readonly DbSession Session;
        internal readonly ProductAttributeDefinitionService Definitions;
        internal readonly ProductMasterMaintenanceService Maintenance;
        internal readonly ProductMasterQueryService Query;
        internal readonly ProductCategoryService Categories;
        internal readonly ProductMasterImportService Import;

        internal Scope(IServiceScope scope, TestUser user)
        {
            _scope = scope;
            Session = Services.GetRequiredService<DbSession>();
            var context = new ServiceBaseContext(Session, NullLoggerFactory.Instance);
            var attributes = Services.GetRequiredService<IProductAttributeRepository>();
            var categories = Services.GetRequiredService<IProductCategoryRepository>();
            var masters = Services.GetRequiredService<IProductMasterRepository>();
            var neutrals = Services.GetRequiredService<IProductNeutralRepository>();
            var suppliers = Services.GetRequiredService<ISupplierRepository>();
            Definitions = new(attributes, categories, user, user, context);
            Maintenance = new(masters, neutrals, categories, suppliers, Services.GetRequiredService<IProductColorRepository>(), Services.GetRequiredService<IProductRepository>(), attributes, context);
            Query = new(masters, neutrals, categories, suppliers, user, user, attributes);
            Categories = new(categories, attributes, context);
            Import = new(masters, categories, suppliers, user, user, attributes, context);
        }
        /// <summary>释放请求事务和连接。</summary>
        public void Dispose() => _scope.Dispose();
    }
}
