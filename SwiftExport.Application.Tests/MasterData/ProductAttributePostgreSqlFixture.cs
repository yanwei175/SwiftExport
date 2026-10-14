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
using SwiftExport.Core.Entities.Systems.Dictionaries.Data;
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
    internal readonly List<Guid> ProfileIds = [];
    internal readonly List<Guid> MasterIds = [];
    internal readonly List<Guid> CustomerProductIds = [];
    internal readonly List<Guid> ColorIds = [];
    internal readonly List<Guid> GroupIds = [];
    internal readonly List<Scope> Scopes = [];
    internal readonly string CodePrefix = "TAG_" + Guid.NewGuid().ToString("N");
    internal ProductCategory Category = null!, OtherCategory = null!;
    internal ProductMaster Master = null!;
    internal SysDictData Profile = null!, OtherProfile = null!, Series = null!;

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
            Assert.AreEqual("dv_next_product_attribute_tests_20261012", await scope.Session.Connection.ExecuteScalarAsync<string>("SELECT current_database()"));
            Assert.AreEqual("192.168.3.12", await scope.Session.Connection.ExecuteScalarAsync<string>("SELECT host(inet_server_addr())"));
            Assert.AreEqual("5432", await scope.Session.Connection.ExecuteScalarAsync<string>("SELECT current_setting('port')"));
            Assert.IsTrue(await scope.Session.Connection.ExecuteScalarAsync<bool>("""
                SELECT to_regclass('public.product_attribute_profile_group') IS NOT NULL
                  AND to_regclass('public.product_attribute_selection') IS NOT NULL
                  AND to_regclass('public.product_master_attribute_selection') IS NULL
                  AND NOT EXISTS(SELECT 1 FROM information_schema.columns
                    WHERE table_schema='public' AND table_name='product_master' AND column_name='product_type')
                  AND EXISTS(SELECT 1 FROM information_schema.columns
                    WHERE table_schema='public' AND table_name='product_master' AND column_name='product_series_id' AND data_type='uuid')
                """), "专用库尚未执行属性模板及统一选择增量迁移；测试不自动初始化或升级结构。");
            fixture.Category = await fixture.AddCategoryAsync(scope, "顶喷");
            fixture.OtherCategory = await fixture.AddCategoryAsync(scope, "另一分类");
            fixture.Profile = await fixture.AddProfileAsync(scope, "顶喷");
            fixture.OtherProfile = await fixture.AddProfileAsync(scope, "另一模板");
            fixture.Series = await fixture.AddProfileAsync(scope, "保留系列", "ProductSeries");
            // 复制库装箱数列仍为 NOT NULL；显式提供夹具值，不为标签测试修改无关运行结构。
            fixture.Master = new ProductMaster { MasterSku = "TAG-" + Guid.NewGuid().ToString("N"), CategoryId = fixture.Category.Uid, DefaultPcsPerPackage = 1,
                ProductAttributeProfileId = fixture.Profile.Uid, ProductSeriesId = fixture.Series.Uid };
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
    internal async Task<ProductCategory> AddCategoryAsync(Scope scope, string name, Guid? parentId = null)
    {
        var category = new ProductCategory { CategoryName = name + Guid.NewGuid().ToString("N"), CategoryCode = Guid.NewGuid().ToString("N"), ParentId = parentId };
        var write = await scope.Services.GetRequiredService<IProductCategoryRepository>().AddAsync(category);
        Assert.IsTrue(write.Success);
        CategoryIds.Add(write.Uid);
        return category;
    }

    /// <summary>创建夹具专属的持久模板或系列字典项，不依赖虚拟内置身份或复制库现有明细。</summary>
    internal async Task<SysDictData> AddProfileAsync(Scope scope, string name, string dictCode = "ProductAttributeProfile")
    {
        var profile = new SysDictData { DictCode = dictCode, DataNameEn = CodePrefix + "_" + Guid.NewGuid().ToString("N"),
            DataNameZh = name, IsEnabled = true, IsSystem = false };
        var write = await scope.Services.GetRequiredService<IRepository<SysDictData>>().AddAsync(profile);
        Assert.IsTrue(write.Success);
        ProfileIds.Add(write.Uid);
        return profile;
    }

    /// <summary>用真实定义用例同批创建组、两个选项及模板关联，返回服务端回执快照。</summary>
    internal async Task<ProductAttributeGroupDto> DefineAsync(Scope scope, Guid? profileId = null,
        ProductAttributeSelectionMode mode = ProductAttributeSelectionMode.Multiple)
    {
        var temporaryId = Guid.NewGuid();
        var request = new SaveProductAttributeDefinitionsRequest { ContractVersion = 2 };
        request.GroupChanges.CreateItems.Add(new() { RowIndex = 1, Data = new() { ClientUid = temporaryId, GroupCode = CodePrefix + "_" + Guid.NewGuid().ToString("N"), GroupName = "安装方式", SelectionMode = mode } });
        request.OptionChanges.CreateItems.Add(new() { RowIndex = 1, Data = new() { AttributeGroupId = temporaryId, OptionCode = "CEILING", OptionName = "吸顶" } });
        request.OptionChanges.CreateItems.Add(new() { RowIndex = 2, Data = new() { AttributeGroupId = temporaryId, OptionCode = "WALL", OptionName = "挂墙", SortOrder = 1 } });
        request.ProfileChanges.CreateItems.Add(new() { Data = new() { AttributeGroupId = temporaryId, ProductAttributeProfileId = profileId ?? Profile.Uid } });
        var result = await SaveAsync(scope, request);
        Assert.IsTrue(result.Success, result.Message);
        Assert.AreEqual(1, result.Data.GroupResults.SuccessCount);
        Assert.AreEqual(2, result.Data.OptionResults.SuccessCount);
        Assert.AreEqual(1, result.Data.ProfileResults.SuccessCount);
        var persistedId = result.Data.GroupResults.Items.Single().Uid!.Value;
        var group = result.Data.Definitions.Groups.Single(row => row.Uid == persistedId);
        Assert.AreNotEqual(temporaryId, group.Uid);
        Assert.IsTrue(group.Options.All(option => option.AttributeGroupId == group.Uid));
        return group;
    }

    /// <summary>调用全局原子维护并捕获本夹具创建的组身份，清理不依赖旧分类归属列。</summary>
    internal async Task<SwiftExport.Contracts.Wrappers.ServiceResult<SaveProductAttributeDefinitionsResponse>> SaveAsync(
        Scope scope, SaveProductAttributeDefinitionsRequest request)
    {
        var result = await scope.Definitions.BatchSaveDefinitionsAsync(request);
        if (result.Success)
            foreach (var item in result.Data.GroupResults.Items.Where(row => row.Success && row.Operation == SwiftExport.Contracts.SysEnums.StaticEnums.SaveOperation.Create))
                if (item.Uid.HasValue && !GroupIds.Contains(item.Uid.Value)) GroupIds.Add(item.Uid.Value);
        return result;
    }

    /// <summary>选项和模板关联的变更也必须提交组聚合的预期版本，不能绕过组版本保护。</summary>
    internal static void TouchGroup(SaveProductAttributeDefinitionsRequest request, ProductAttributeGroupDto group)
    {
        request.GroupChanges.UpdateItems.Add(new() { Uid = group.Uid, Version = group.Version,
            Data = new() { GroupCode = group.GroupCode, GroupName = group.GroupName, SelectionMode = group.SelectionMode, SortOrder = group.SortOrder } });
    }

    /// <summary>从服务器当前头部复制详情请求；默认不改变旧系列。</summary>
    internal async Task<SaveProductMasterDetailRequest> DetailRequestAsync(Scope scope)
    {
        var detail = await scope.Query.GetDetailAsync(Master.Uid);
        Assert.IsTrue(detail.Success, detail.Message);
        return new() { ContractVersion = 3, ProductMasterId = Master.Uid, ExpectedVersion = detail.Data.Version, Master = detail.Data.Master.Adapt<SwiftExport.Contracts.DTOs.Products.ProductMasters.ProductMasterWriteDto>() };
    }

    /// <summary>只移除捕获的测试分类、主型号及其自建标签；保留复制库的其他数据和结构。</summary>
    public async ValueTask DisposeAsync()
    {
        foreach (var scope in Scopes) scope.Dispose();
        if (CategoryIds.Count == 0 && ProfileIds.Count == 0 && MasterIds.Count == 0 && CustomerProductIds.Count == 0 && ColorIds.Count == 0 && GroupIds.Count == 0)
        {
            // 目标或结构守卫失败时没有夹具写入，不进入任何清理 SQL，保留原始拒绝证据。
            await Provider.DisposeAsync();
            return;
        }
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await connection.ExecuteAsync("""
            DELETE FROM public.product_attribute_selection WHERE (owner_type=1 AND owner_id=ANY(@Masters))
               OR option_id IN (SELECT uid FROM public.product_attribute_option WHERE attribute_group_id=ANY(@Groups));
            DELETE FROM public.product_attribute_profile_group WHERE product_attribute_profile_id=ANY(@Profiles) OR attribute_group_id=ANY(@Groups);
            DELETE FROM public.product_attribute_option WHERE attribute_group_id=ANY(@Groups);
            DELETE FROM public.product_attribute_group WHERE uid=ANY(@Groups);
            DELETE FROM public.customer_product WHERE uid=ANY(@CustomerProducts);
            DELETE FROM public.product_neutral WHERE product_master_id=ANY(@Masters);
            DELETE FROM public.product_master WHERE uid=ANY(@Masters);
            DELETE FROM public.product_color WHERE uid=ANY(@Colors);
            DELETE FROM public.sys_dict_data WHERE uid=ANY(@Profiles);
            DELETE FROM public.product_category WHERE uid=ANY(@Categories);
            """, new { Masters = MasterIds.ToArray(), CustomerProducts = CustomerProductIds.ToArray(), Categories = CategoryIds.ToArray(), Profiles = ProfileIds.ToArray(), Colors = ColorIds.ToArray(), Groups = GroupIds.ToArray() }, transaction);
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
            Definitions = new(attributes, user, user, context);
            Maintenance = new(masters, neutrals, categories, suppliers, Services.GetRequiredService<IProductColorRepository>(), Services.GetRequiredService<IProductRepository>(), attributes, context);
            Query = new(masters, neutrals, attributes);
            Categories = new(categories, context);
            Import = new(masters, categories, suppliers, Services.GetRequiredService<SwiftExport.Core.Interfaces.Systems.Dictionaries.Data.ISysDictDataRepository>(), user, user, context);
        }
        /// <summary>释放请求事务和连接。</summary>
        public void Dispose() => _scope.Dispose();
    }
}
