using SwiftExport.Core.Interfaces.Products.Catalog;
using SwiftExport.Core.Interfaces.Products.Categories;
using SwiftExport.Core.Interfaces.Products.Colors;
using SwiftExport.Core.Interfaces.Products.ProductMasters;
using SwiftExport.Core.Interfaces.Products.ProductNeutrals;
using SwiftExport.Core.Interfaces.Suppliers.Management;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Services.Products.ProductMasters.Maintenance;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Products.ProductMasters;
using SwiftExport.Contracts.DTOs.Products.ProductMasters.Requests;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Entities.Inventory;
using SwiftExport.Core.Interfaces;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Infrastructure;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Application.Tests.Database;

namespace SwiftExport.Application.Tests.MasterData;

/// <summary>在获批单PG测试库执行真实保存与主动拉取服务，验证独立资料、版本和事务边界。</summary>
[TestClass]
[DoNotParallelize]
public sealed class ProductMasterSynchronizationSqlTests
{
    private PostgreSqlLegacyTestDatabase? _database;
    /// <summary>关联本例完整事实导出。</summary>
    public TestContext TestContext { get; set; } = default!;
    private ServiceProvider _provider = null!;
    private IServiceScope _scope = null!;
    private DbSession _session = null!;
    private ProductMasterMaintenanceService _service = null!;
    private IProductRepository _products = null!;
    private Guid _masterId;
    private Guid _supplierId;
    private Guid _colorId;

    /// <summary>显式开关启用真实 SQL 验证；复用固定实体基线。</summary>
    [TestInitialize]
    public async Task InitializeAsync()
    {
        if (Environment.GetEnvironmentVariable("SWIFTEXPORT_RUN_PG_LEGACY_TESTS") != "1")
            Assert.Inconclusive("设置 SWIFTEXPORT_RUN_PG_LEGACY_TESTS=1 后运行获批单PG同步测试。");
        _database = await PostgreSqlLegacyTestDatabase.AcquireAsync(TestContext);
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = _database.ConnectionString
        }).Build());
        services.AddSingleton<ICurrentUser>(new TestUser());
        services.AddInfrastructureServices();
        _provider = services.BuildServiceProvider();
        _scope = _provider.CreateScope();
        var provider = _scope.ServiceProvider;
        _session = provider.GetRequiredService<DbSession>();
        // 本例复用固定PG实体基线，不再生成旧提供者的宽松临时表。
        _products = provider.GetRequiredService<IProductRepository>();
        _service = new ProductMasterMaintenanceService(provider.GetRequiredService<IProductMasterRepository>(),
            provider.GetRequiredService<IProductNeutralRepository>(), provider.GetRequiredService<IProductCategoryRepository>(),
            provider.GetRequiredService<ISupplierRepository>(), provider.GetRequiredService<IProductColorRepository>(), _products,
            provider.GetRequiredService<SwiftExport.Core.Interfaces.Products.Attributes.IProductAttributeRepository>(),
            new ServiceBaseContext(_session, NullLoggerFactory.Instance));
        _masterId = (await provider.GetRequiredService<IProductMasterRepository>().AddAsync(new ProductMaster { MasterSku = "OLD" })).Uid;
        _supplierId = (await provider.GetRequiredService<ISupplierRepository>().AddAsync(new Supplier
        {
            Uid = _supplierId, SupplierCode = "SERVER-SUP", SupplierName = "服务端供应商", DocRelateNo = "TEST"
        })).Uid;
        // 完整实体基线要求颜色编码；补齐夹具身份列，原同步场景仍只引用颜色Uid。
        _colorId = (await provider.GetRequiredService<IProductColorRepository>().AddAsync(new ProductColor
        { ColorCodeUnique = "MASTER-SYNC-TEST", ColorCode = "TEST" })).Uid;
    }

    /// <summary>释放本例连接并完整导出事实，仅重置获批新库public结构。</summary>
    [TestCleanup]
    public async Task CleanupAsync()
    {
        _scope?.Dispose();
        if (_provider is not null) await _provider.DisposeAsync();
        if (_database is not null) await _database.DisposeAsync();
    }

    /// <summary>主型号两保存入口仅更新自身资料；关联物料描述、配置、供应商、重量体积及审计版本均保持原值。</summary>
    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task Save_DoesNotOverwriteLinkedProducts(bool detail)
    {
        var active = await AddProductAsync(_masterId);
        var inactive = await AddProductAsync(_masterId, active: false);
        // 审计基线从数据库读回，避免 timestamp(6) 的精度转换被误判为同步更新。
        active = (await _products.GetByIdAsync(active.Uid))!;
        inactive = (await _products.GetByIdAsync(inactive.Uid))!;
        var deleted = await AddProductAsync(_masterId, deleted: true);
        var unlinked = await AddProductAsync(null);
        var other = await AddProductAsync(Guid.NewGuid());
        Assert.IsTrue(await SaveAsync(detail));
        foreach (var id in new[] { active.Uid, inactive.Uid })
        {
            var saved = (await _products.GetByIdAsync(id))!;
            Assert.AreEqual("NEW", (await _products.GetDtoByIdAsync(id))!.MasterSku);
            Assert.AreEqual(_masterId, saved.ProductMasterId);
            Assert.AreEqual("原描述", saved.DescriptionEn);
            Assert.AreEqual("原中文", saved.DescriptionZh);
            Assert.AreEqual("原配置", saved.FeaturesEn);
            Assert.AreEqual("原中文配置", saved.FeaturesZh);
            Assert.AreEqual(active.DefaultSupplierId, saved.DefaultSupplierId);
            Assert.AreEqual(null,
                (await _products.GetDtoByIdAsync(id))!.DefaultSupplierCode);
            Assert.AreEqual(1, saved.Version);
            Assert.AreEqual(id == active.Uid ? active.UpdatedBy : inactive.UpdatedBy, saved.UpdatedBy);
            Assert.AreEqual(id == active.Uid ? active.UpdatedDate : inactive.UpdatedDate, saved.UpdatedDate);
            Assert.AreEqual(99m, saved.CNYPrice);
            Assert.IsNull(saved.UnitWeight);
            Assert.IsNull(saved.UnitCBM);
        }
        Assert.IsFalse((await _products.GetByIdAsync(inactive.Uid))!.IsActive);
        foreach (var id in new[] { deleted.Uid, unlinked.Uid, other.Uid })
        {
            // 已删除物料和失效主型号的显示值为空，持久化物料仍仅按身份核验版本。
            var saved = await _session.Connection.QuerySingleAsync<Product>("SELECT uid AS \"Uid\",version AS \"Version\" FROM public.product WHERE uid=@id", new { id });
            Assert.IsNull((await _products.GetDtoByIdAsync(id))?.MasterSku);
            Assert.AreEqual(1, saved.Version);
        }
        // 主型号编码由投影读取；保存主型号不会使物料现有版本失效。
        var stale = await _products.UpdateAsync(active);
        Assert.IsTrue(stale.Success, stale.Message);
    }

    /// <summary>主型号或明细旧版本拒绝整个保存，不更改关联物料。</summary>
    [TestMethod]
    [DataRow("master-detail")] [DataRow("master-batch")] [DataRow("neutral")]
    public async Task VersionConflict_DoesNotSynchronize(string scenario)
    {
        var product = await AddProductAsync(_masterId);
        var request = DetailRequest();
        if (scenario == "neutral")
        {
            var row = new ProductNeutral { ProductMasterId = _masterId, ProductCode = "N", ColorId = _colorId };
            await _scope.ServiceProvider.GetRequiredService<IProductNeutralRepository>().AddAsync(row);
            request.UpdateNeutrals.Add(new() { Uid = row.Uid, Version = 99, Data = new() { ProductCode = "N", ColorId = _colorId } });
        }
        else request.ExpectedVersion = 99;
        var code = scenario == "master-batch"
            ? (await _service.BatchSaveAsync(BatchRequest(request))).Error.Code
            : (await _service.SaveDetailAsync(request)).Error.Code;
        Assert.AreEqual(ServiceErrorCodes.VersionConflict, code);
        Assert.AreEqual(1, (await _products.GetByIdAsync(product.Uid))!.Version);
    }

    /// <summary>无关联可以新建；删除主型号不删除或同步 Product。</summary>
    [TestMethod]
    public async Task CreateWithoutLinks_AndDelete_DoNotMutateProducts()
    {
        var product = await AddProductAsync(_masterId);
        var create = DetailRequest();
        create.ProductMasterId = Guid.Empty;
        create.ExpectedVersion = 0;
        Assert.IsTrue((await _service.SaveDetailAsync(create)).Success);
        var deleted = await _service.BatchSaveAsync(new()
        {
            ContractVersion = 3,
            DeleteItems = [new() { Uid = _masterId, Version = 1 }]
        });
        Assert.IsTrue(deleted.Success, deleted.Message);
        Assert.AreEqual(1, (await _products.GetByIdAsync(product.Uid))!.Version);
    }

    /// <summary>五组拉取只更新所选产品；同主型号的未选产品及重复拉取均不增加版本。</summary>
    [TestMethod]
    [DataRow("descriptions")]
    [DataRow("features")]
    [DataRow("supplier")]
    [DataRow("weight-volume")]
    [DataRow("all")]
    public async Task PullSelectedProducts_UpdatesOnlyChosenProductAndFields(string group)
    {
        await _session.Connection.ExecuteAsync("""
            UPDATE public.product_master SET default_description_en='新英文', default_description_zh=NULL,
                default_features_en='新配置', default_features_zh=NULL, default_supplier_id=@supplierId,
                default_gross_weight=12.5, default_cbm=0.75 WHERE uid=@masterId
            """, new { supplierId = _supplierId, masterId = _masterId });
        var selected = await AddProductAsync(_masterId, active: false);
        var untouched = await AddProductAsync(_masterId);
        var fields = group switch
        {
            "descriptions" => ProductMasterSyncFields.Descriptions,
            "features" => ProductMasterSyncFields.Features,
            "supplier" => ProductMasterSyncFields.Supplier,
            "weight-volume" => ProductMasterSyncFields.WeightVolume,
            _ => ProductMasterSyncFields.All
        };
        var request = new PullProductMasterDataRequest { ProductIds = [selected.Uid] };
        var result = await _service.PullProductsAsync(request, fields);
        Assert.IsTrue(result.Success, result.Message);
        Assert.AreEqual(1, result.Data.SelectedProductCount);
        Assert.AreEqual(0, result.Data.SkippedProductCount);
        Assert.AreEqual(1, result.Data.UpdatedProductCount);
        var saved = (await _products.GetByIdAsync(selected.Uid))!;
        Assert.AreEqual(fields.HasFlag(ProductMasterSyncFields.Descriptions) ? "新英文" : "原描述", saved.DescriptionEn);
        Assert.AreEqual(fields.HasFlag(ProductMasterSyncFields.Descriptions) ? null : "原中文", saved.DescriptionZh);
        Assert.AreEqual(fields.HasFlag(ProductMasterSyncFields.Features) ? "新配置" : "原配置", saved.FeaturesEn);
        Assert.AreEqual(fields.HasFlag(ProductMasterSyncFields.Features) ? null : "原中文配置", saved.FeaturesZh);
        Assert.AreEqual(fields.HasFlag(ProductMasterSyncFields.Supplier) ? _supplierId : Guid.Empty, saved.DefaultSupplierId);
        Assert.AreEqual(fields.HasFlag(ProductMasterSyncFields.WeightVolume) ? 12.5m : null, saved.UnitWeight);
        Assert.AreEqual(fields.HasFlag(ProductMasterSyncFields.WeightVolume) ? 0.75m : null, saved.UnitCBM);
        Assert.AreEqual("OLD", (await _products.GetDtoByIdAsync(selected.Uid))!.MasterSku);
        Assert.AreEqual(2, saved.Version);
        Assert.AreEqual(1, (await _products.GetByIdAsync(untouched.Uid))!.Version);
        var repeated = await _service.PullProductsAsync(request, fields);
        Assert.IsTrue(repeated.Success, repeated.Message);
        Assert.AreEqual(0, repeated.Data.UpdatedProductCount);
        Assert.AreEqual(2, (await _products.GetByIdAsync(selected.Uid))!.Version);
    }

    /// <summary>无关联及失效主型号按产品计数跳过；请求中不存在的目标产品整批回滚。</summary>
    [TestMethod]
    public async Task PullSelectedProducts_SkipsUnavailableSourcesAndRejectsMissingTargets()
    {
        var valid = await AddProductAsync(_masterId);
        var unlinked = await AddProductAsync(null);
        var missingMaster = await AddProductAsync(Guid.NewGuid());
        var deletedMasterId = (await _scope.ServiceProvider.GetRequiredService<IProductMasterRepository>()
            .AddAsync(new ProductMaster { MasterSku = "DELETED" })).Uid;
        var deletedMasterProduct = await AddProductAsync(deletedMasterId);
        await _session.Connection.ExecuteAsync("UPDATE public.product_master SET is_deleted=TRUE WHERE uid=@id", new { id = deletedMasterId });
        var request = new PullProductMasterDataRequest { ProductIds = [valid.Uid, unlinked.Uid, missingMaster.Uid, deletedMasterProduct.Uid] };
        var result = await _service.PullProductsAsync(request, ProductMasterSyncFields.Descriptions);
        Assert.IsTrue(result.Success, result.Message);
        Assert.AreEqual(4, result.Data.SelectedProductCount);
        Assert.AreEqual(3, result.Data.SkippedProductCount);
        Assert.AreEqual(1, result.Data.UpdatedProductCount);
        var invalid = await _service.PullProductsAsync(new() { ProductIds = [valid.Uid, Guid.NewGuid()] }, ProductMasterSyncFields.Features);
        Assert.IsFalse(invalid.Success);
        Assert.AreEqual(2, (await _products.GetByIdAsync(valid.Uid))!.Version);
        Assert.IsNull(_session.Transaction);
    }

    /// <summary>无效供应商阻断整个所选批次，其他字段及审计版本均不写入。</summary>
    [TestMethod]
    public async Task PullSelectedProducts_InvalidSupplierRollsBackBatch()
    {
        var first = await AddProductAsync(_masterId);
        var second = await AddProductAsync(_masterId);
        await _session.Connection.ExecuteAsync("UPDATE public.product_master SET default_supplier_id=@supplierId WHERE uid=@masterId",
            new { supplierId = _supplierId, masterId = _masterId });
        await _session.Connection.ExecuteAsync("UPDATE public.supplier SET is_active=FALSE WHERE uid=@id", new { id = _supplierId });
        var result = await _service.PullProductsAsync(new() { ProductIds = [first.Uid, second.Uid] }, ProductMasterSyncFields.All);
        Assert.IsFalse(result.Success);
        Assert.AreEqual(1, (await _products.GetByIdAsync(first.Uid))!.Version);
        Assert.AreEqual(1, (await _products.GetByIdAsync(second.Uid))!.Version);
        Assert.IsNull(_session.Transaction);
    }

    /// <summary>构造有意与主型号不同的资料；相同旧供应商便于精确验证未勾选保留。</summary>
    private async Task<Product> AddProductAsync(Guid? masterId, bool active = true, bool deleted = false)
    {
        var product = new Product
        {
            ProductMasterId = masterId, Sku = Guid.NewGuid().ToString("N"),
            SupplyType = "采购", IsActive = active, IsDeleted = deleted,
            DescriptionEn = "原描述", DescriptionZh = "原中文", FeaturesEn = "原配置", FeaturesZh = "原中文配置",
            DefaultSupplierId = Guid.Empty, CNYPrice = 99m
        };
        var result = await _products.AddAsync(product);
        Assert.IsTrue(result.Success, result.Message);
        if (deleted)
            await _session.Connection.ExecuteAsync("UPDATE public.product SET is_deleted=TRUE WHERE uid=@Uid", new { product.Uid });
        return product;
    }

    private SaveProductMasterDetailRequest DetailRequest() => new()
    {
        ContractVersion = 3,
        ProductMasterId = _masterId, ExpectedVersion = 1,
        Master = new() { MasterSku = " NEW ", DefaultDescriptionEn = " 英文描述 ", DefaultDescriptionZh = "中文描述",
            DefaultFeaturesEn = "英文配置", DefaultFeaturesZh = " ", DefaultSupplierId = _supplierId }
    };

    private static ProductMasterBatchSaveRequest BatchRequest(SaveProductMasterDetailRequest request) => new()
    {
        ContractVersion = 3,
        UpdateItems = [new() { Uid = request.ProductMasterId, Version = request.ExpectedVersion, Data = request.Master }]
    };

    private async Task<bool> SaveAsync(bool detail)
    {
        var request = DetailRequest();
        return detail ? (await _service.SaveDetailAsync(request)).Success : (await _service.BatchSaveAsync(BatchRequest(request))).Success;
    }

    /// <summary>固定审计身份，测试无需认证服务器。</summary>
    private sealed class TestUser : ICurrentUser
    {
        public string UserName => "master-sync-test";
        public Guid? UserId => null;
        public bool IsAuthenticated => true;
    }
}
