using SwiftExport.Core.Interfaces.Products.Catalog;
using SwiftExport.Core.Interfaces.Products.Categories;
using SwiftExport.Core.Interfaces.Products.Colors;
using SwiftExport.Core.Interfaces.Products.ProductMasters;
using SwiftExport.Core.Interfaces.Products.ProductNeutrals;
using SwiftExport.Core.Interfaces.Suppliers.Management;
using System.ComponentModel.DataAnnotations.Schema;
using System.Reflection;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.Data.SqlClient;
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
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Entities.Inventory;
using SwiftExport.Core.Interfaces;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Infrastructure;
using SwiftExport.Infrastructure.Data;

namespace SwiftExport.Application.Tests.MasterData;

/// <summary>在唯一 LocalDB 测试库执行真实服务及仓储，验证资料同步、版本和事务回滚，不读取业务连接配置。</summary>
[TestClass]
[DoNotParallelize]
public sealed class ProductMasterSynchronizationSqlTests
{
    private string _database = string.Empty;
    private bool _created;
    private ServiceProvider _provider = null!;
    private IServiceScope _scope = null!;
    private DbSession _session = null!;
    private ProductMasterMaintenanceService _service = null!;
    private IProductRepository _products = null!;
    private Guid _masterId;
    private Guid _supplierId;
    private Guid _colorId;

    /// <summary>显式开关启用真实 SQL 验证；只创建本测试拥有的随机库与最小实体表。</summary>
    [TestInitialize]
    public async Task InitializeAsync()
    {
        if (Environment.GetEnvironmentVariable("SWIFTEXPORT_RUN_SQL_TESTS") != "1")
            Assert.Inconclusive("设置 SWIFTEXPORT_RUN_SQL_TESTS=1 后运行隔离 LocalDB 同步测试。");
        _database = "SwiftExport_MasterSyncTests_" + Guid.NewGuid().ToString("N");
        await using (var connection = new SqlConnection(ConnectionString("master")))
        {
            await connection.OpenAsync();
            await connection.ExecuteAsync($"CREATE DATABASE [{_database}];");
            _created = true;
        }
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = ConnectionString(_database)
        }).Build());
        services.AddSingleton<ICurrentUser>(new TestUser());
        services.AddInfrastructureServices();
        _provider = services.BuildServiceProvider();
        _scope = _provider.CreateScope();
        var provider = _scope.ServiceProvider;
        _session = provider.GetRequiredService<DbSession>();
        // 测试 schema 仅承载真实仓储及产品详情投影需要的表，不是生产 DDL 或迁移；不会修改 FreshInstall。
        foreach (var type in new[] { typeof(Product), typeof(ProductMaster), typeof(ProductNeutral), typeof(ProductCategory), typeof(ProductColor), typeof(Supplier), typeof(WarehouseSite), typeof(ProductPackage) })
            await _session.Connection.ExecuteAsync(CreateTable(type));
        _products = provider.GetRequiredService<IProductRepository>();
        _service = new ProductMasterMaintenanceService(provider.GetRequiredService<IProductMasterRepository>(),
            provider.GetRequiredService<IProductNeutralRepository>(), provider.GetRequiredService<IProductCategoryRepository>(),
            provider.GetRequiredService<ISupplierRepository>(), provider.GetRequiredService<IProductColorRepository>(), _products,
            new ServiceBaseContext(_session, NullLoggerFactory.Instance));
        _masterId = (await provider.GetRequiredService<IProductMasterRepository>().AddAsync(new ProductMaster { MasterSku = "OLD" })).Uid;
        _supplierId = (await provider.GetRequiredService<ISupplierRepository>().AddAsync(new Supplier
        {
            Uid = _supplierId, SupplierCode = "SERVER-SUP", SupplierName = "服务端供应商", DocRelateNo = "TEST"
        })).Uid;
        _colorId = (await provider.GetRequiredService<IProductColorRepository>().AddAsync(new ProductColor())).Uid;
    }

    /// <summary>仅清理当前测试新建且名称验证通过的随机库，绝不针对配置中的既有数据库。</summary>
    [TestCleanup]
    public async Task CleanupAsync()
    {
        _scope?.Dispose();
        if (_provider is not null) await _provider.DisposeAsync();
        if (!_created) return;
        Assert.IsTrue(Regex.IsMatch(_database, "^SwiftExport_MasterSyncTests_[0-9a-f]{32}$"));
        await using var connection = new SqlConnection(ConnectionString("master"));
        await connection.OpenAsync();
        await connection.ExecuteAsync($"ALTER DATABASE [{_database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_database}];");
    }

    /// <summary>两入口四种选项按 UID 更新且不串组；停用物料参与，删除和未关联物料保持不变。</summary>
    [TestMethod]
    [DataRow(false, false, false)] [DataRow(false, true, false)]
    [DataRow(false, false, true)] [DataRow(false, true, true)]
    [DataRow(true, false, false)] [DataRow(true, true, false)]
    [DataRow(true, false, true)] [DataRow(true, true, true)]
    public async Task Save_UpdatesOnlyLinkedProducts(bool detail, bool descriptions, bool supplier)
    {
        var active = await AddProductAsync(_masterId);
        var inactive = await AddProductAsync(_masterId, active: false);
        var deleted = await AddProductAsync(_masterId, deleted: true);
        var unlinked = await AddProductAsync(null);
        var other = await AddProductAsync(Guid.NewGuid());
        Assert.IsTrue(await SaveAsync(detail, descriptions, supplier));
        foreach (var id in new[] { active.Uid, inactive.Uid })
        {
            var saved = (await _products.GetByIdAsync(id))!;
            Assert.AreEqual("NEW", saved.MasterSku);
            Assert.AreEqual(_masterId, saved.ProductMasterId);
            Assert.AreEqual(descriptions ? "英文描述" : "原描述", saved.DescriptionEn);
            Assert.AreEqual(descriptions ? "中文描述" : "原中文", saved.DescriptionZh);
            Assert.AreEqual(descriptions ? "英文配置" : "原配置", saved.FeaturesEn);
            Assert.AreEqual(descriptions ? null : "原中文配置", saved.FeaturesZh);
            Assert.AreEqual(supplier ? _supplierId : active.DefaultSupplierId, saved.DefaultSupplierId);
            Assert.AreEqual(supplier ? "SERVER-SUP" : null,
                (await _products.GetDtoByIdAsync(id))!.DefaultSupplierCode);
            Assert.AreEqual(2, saved.Version);
            Assert.AreEqual("master-sync-test", saved.UpdatedBy);
            Assert.IsNotNull(saved.UpdatedDate);
            Assert.AreEqual(99m, saved.CNYPrice);
        }
        Assert.IsFalse((await _products.GetByIdAsync(inactive.Uid))!.IsActive);
        foreach (var id in new[] { deleted.Uid, unlinked.Uid, other.Uid })
        {
            var saved = await _session.Connection.QuerySingleAsync<Product>("SELECT * FROM dbo.Product WHERE Uid=@id", new { id });
            Assert.AreEqual("OLD", saved.MasterSku);
            Assert.AreEqual(1, saved.Version);
        }
        // 同步已增加版本，原来打开的物料编辑对象不能覆盖服务端同步。
        var stale = await _products.UpdateAsync(active);
        Assert.IsFalse(stale.Success);
        Assert.AreEqual("CONCURRENCY_CONFLICT", stale.ErrorCode);
    }

    /// <summary>勾选组的空值清除，两次相同提交不会对无差异物料重复打戳。</summary>
    [TestMethod]
    public async Task EmptyValues_ClearSelectedGroups_AndNoOpPreservesVersion()
    {
        var product = await AddProductAsync(_masterId);
        var request = DetailRequest(true, true);
        request.Master = new() { MasterSku = " OLD ", DefaultDescriptionEn = "   " };
        var result = await _service.SaveDetailAsync(request);
        Assert.IsTrue(result.Success, result.Message);
        var saved = (await _products.GetByIdAsync(product.Uid))!;
        Assert.IsNull(saved.DescriptionEn);
        Assert.IsNull(saved.DescriptionZh);
        Assert.IsNull(saved.FeaturesEn);
        Assert.IsNull(saved.FeaturesZh);
        Assert.IsNull(saved.DefaultSupplierId);
        Assert.IsNull((await _products.GetDtoByIdAsync(product.Uid))!.DefaultSupplierCode);
        request.ExpectedVersion = result.Data.Detail.Version;
        Assert.IsTrue((await _service.SaveDetailAsync(request)).Success);
        var unchanged = (await _products.GetByIdAsync(product.Uid))!;
        Assert.AreEqual(saved.Version, unchanged.Version);
        Assert.AreEqual(saved.UpdatedDate, unchanged.UpdatedDate);
    }

    /// <summary>任一同步 SQL 失败回滚主型号、Neutral 和前面已写入的物料；两个入口一致。</summary>
    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task SyncFailure_RollsBackAggregate(bool detail)
    {
        var product = await AddProductAsync(_masterId);
        await _session.Connection.ExecuteAsync("ALTER TABLE dbo.Product ADD CONSTRAINT CK_TestSyncFailure CHECK (MasterSku <> N'NEW');");
        Assert.IsFalse(await SaveAsync(detail, true, true, addNeutral: detail));
        Assert.AreEqual("OLD", (await _products.GetByIdAsync(product.Uid))!.MasterSku);
        Assert.AreEqual(1, await _session.Connection.ExecuteScalarAsync<int>("SELECT Version FROM dbo.ProductMaster WHERE Uid=@id", new { id = _masterId }));
        Assert.AreEqual(0, await _session.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.ProductNeutral"));
        Assert.IsNull(_session.Transaction);
    }

    /// <summary>无效供应商在整次事务内拒绝，未授权供应商同步时不新增启用校验。</summary>
    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task InactiveSupplier_RejectsOnlySelectedSynchronization(bool detail)
    {
        await AddProductAsync(_masterId);
        await _session.Connection.ExecuteAsync("UPDATE dbo.Supplier SET IsActive=0 WHERE Uid=@id", new { id = _supplierId });
        Assert.IsFalse(await SaveAsync(detail, false, true));
        Assert.AreEqual(1, await _session.Connection.ExecuteScalarAsync<int>("SELECT Version FROM dbo.ProductMaster WHERE Uid=@id", new { id = _masterId }));
        Assert.IsTrue(await SaveAsync(detail, false, false));
    }

    /// <summary>主型号或明细旧版本拒绝整个保存，不更改关联物料。</summary>
    [TestMethod]
    [DataRow("master-detail")] [DataRow("master-batch")] [DataRow("neutral")]
    public async Task VersionConflict_DoesNotSynchronize(string scenario)
    {
        var product = await AddProductAsync(_masterId);
        var request = DetailRequest(true, true);
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
        var create = DetailRequest(true, true);
        create.ProductMasterId = Guid.Empty;
        create.ExpectedVersion = 0;
        Assert.IsTrue((await _service.SaveDetailAsync(create)).Success);
        var deleted = await _service.BatchSaveAsync(new()
        {
            UpdateProductDescriptions = true, UpdateProductDefaultSupplier = true,
            DeleteItems = [new() { Uid = _masterId, Version = 1 }]
        });
        Assert.IsTrue(deleted.Success, deleted.Message);
        Assert.AreEqual(1, (await _products.GetByIdAsync(product.Uid))!.Version);
    }

    /// <summary>后一个主型号同步失败，回滚前一个已经同步的物料及同批新增主型号。</summary>
    [TestMethod]
    public async Task LaterBatchSyncFailure_RollsBackEarlierSyncAndCreate()
    {
        var other = (await _scope.ServiceProvider.GetRequiredService<IProductMasterRepository>()
            .AddAsync(new ProductMaster { MasterSku = "OTHER" })).Uid;
        var ids = new[] { _masterId, other }.OrderBy(id => id).ToArray();
        var first = await AddProductAsync(ids[0]);
        var last = await AddProductAsync(ids[1]);
        await _session.Connection.ExecuteAsync("ALTER TABLE dbo.Product ADD CONSTRAINT CK_TestLaterFailure CHECK (DescriptionEn <> N'FAIL');");
        var result = await _service.BatchSaveAsync(new()
        {
            UpdateProductDescriptions = true,
            CreateItems = [new() { Data = new() { MasterSku = "CREATED" } }],
            UpdateItems =
            [
                new() { Uid = ids[0], Version = 1, Data = new() { MasterSku = "FIRST", DefaultDescriptionEn = "OK" } },
                new() { Uid = ids[1], Version = 1, Data = new() { MasterSku = "LAST", DefaultDescriptionEn = "FAIL" } }
            ]
        });
        Assert.IsFalse(result.Success);
        Assert.AreEqual(1, (await _products.GetByIdAsync(first.Uid))!.Version);
        Assert.AreEqual("原描述", (await _products.GetByIdAsync(first.Uid))!.DescriptionEn);
        Assert.AreEqual(1, (await _products.GetByIdAsync(last.Uid))!.Version);
        Assert.AreEqual(2, await _session.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.ProductMaster"));
        Assert.AreEqual(0, await _session.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.ProductMaster WHERE Version<>1"));
    }

    /// <summary>五种独立下发从数据库读取现值，只更新所选字段组及关联产品；重复下发不改变版本。</summary>
    [TestMethod]
    [DataRow("descriptions")]
    [DataRow("features")]
    [DataRow("supplier")]
    [DataRow("weight-volume")]
    [DataRow("all")]
    public async Task IndependentSync_UpdatesSelectedGroupsOnly(string group)
    {
        await _session.Connection.ExecuteAsync("""
            UPDATE dbo.ProductMaster SET DefaultDescriptionEn=N'新英文', DefaultDescriptionZh=N'新中文',
                DefaultFeaturesEn=N'新配置', DefaultFeaturesZh=NULL, DefaultSupplierId=@supplierId,
                DefaultGrossWeight=12.5, DefaultCbm=0.75 WHERE Uid=@masterId
            """, new { supplierId = _supplierId, masterId = _masterId });
        var active = await AddProductAsync(_masterId);
        var inactive = await AddProductAsync(_masterId, active: false);
        var deleted = await AddProductAsync(_masterId, deleted: true);
        var unlinked = await AddProductAsync(null);
        var fields = group switch
        {
            "descriptions" => ProductMasterSyncFields.Descriptions,
            "features" => ProductMasterSyncFields.Features,
            "supplier" => ProductMasterSyncFields.Supplier,
            "weight-volume" => ProductMasterSyncFields.WeightVolume,
            _ => ProductMasterSyncFields.All
        };
        var request = new SyncProductMasterDataRequest { ProductMasterIds = [_masterId] };
        var result = await _service.SyncProductsAsync(request, fields);
        Assert.IsTrue(result.Success, result.Message);
        Assert.AreEqual(1, result.Data.ProductMasterCount);
        Assert.AreEqual(2, result.Data.UpdatedProductCount);
        foreach (var id in new[] { active.Uid, inactive.Uid })
        {
            var saved = (await _products.GetByIdAsync(id))!;
            Assert.AreEqual(fields.HasFlag(ProductMasterSyncFields.Descriptions) ? "新英文" : "原描述", saved.DescriptionEn);
            Assert.AreEqual(fields.HasFlag(ProductMasterSyncFields.Descriptions) ? "新中文" : "原中文", saved.DescriptionZh);
            Assert.AreEqual(fields.HasFlag(ProductMasterSyncFields.Features) ? "新配置" : "原配置", saved.FeaturesEn);
            Assert.AreEqual(fields.HasFlag(ProductMasterSyncFields.Features) ? null : "原中文配置", saved.FeaturesZh);
            Assert.AreEqual(fields.HasFlag(ProductMasterSyncFields.Supplier) ? _supplierId : Guid.Empty, saved.DefaultSupplierId);
            Assert.AreEqual(fields.HasFlag(ProductMasterSyncFields.WeightVolume) ? 12.5m : null, saved.UnitWeight);
            Assert.AreEqual(fields.HasFlag(ProductMasterSyncFields.WeightVolume) ? 0.75m : null, saved.UnitCBM);
            Assert.AreEqual("OLD", saved.MasterSku);
            Assert.AreEqual(2, saved.Version);
        }
        foreach (var id in new[] { deleted.Uid, unlinked.Uid })
            Assert.AreEqual(1, await _session.Connection.ExecuteScalarAsync<int>("SELECT Version FROM dbo.Product WHERE Uid=@id", new { id }));
        var repeated = await _service.SyncProductsAsync(request, fields);
        Assert.IsTrue(repeated.Success, repeated.Message);
        Assert.AreEqual(0, repeated.Data.UpdatedProductCount);
        Assert.AreEqual(2, (await _products.GetByIdAsync(active.Uid))!.Version);
    }

    /// <summary>多个主型号合并下发；无关联源成功返回零，空值照实清除。</summary>
    [TestMethod]
    public async Task IndependentSync_MultipleSources_AndEmptyValues()
    {
        var otherId = (await _scope.ServiceProvider.GetRequiredService<IProductMasterRepository>()
            .AddAsync(new ProductMaster { MasterSku = "OTHER", DefaultDescriptionEn = "OTHER-DESC" })).Uid;
        var first = await AddProductAsync(_masterId);
        var second = await AddProductAsync(otherId);
        var request = new SyncProductMasterDataRequest { ProductMasterIds = [_masterId, otherId] };
        var result = await _service.SyncProductsAsync(request, ProductMasterSyncFields.Descriptions | ProductMasterSyncFields.Supplier);
        Assert.IsTrue(result.Success, result.Message);
        Assert.AreEqual(2, result.Data.UpdatedProductCount);
        Assert.IsNull((await _products.GetByIdAsync(first.Uid))!.DescriptionEn);
        Assert.AreEqual("OTHER-DESC", (await _products.GetByIdAsync(second.Uid))!.DescriptionEn);
        Assert.IsNull((await _products.GetByIdAsync(first.Uid))!.DefaultSupplierId);
        var noLinks = await _service.SyncProductsAsync(new() { ProductMasterIds = [Guid.NewGuid()] }, ProductMasterSyncFields.Features);
        Assert.IsFalse(noLinks.Success, "不存在的来源不能被当成无关联来源。");
        var emptySource = (await _scope.ServiceProvider.GetRequiredService<IProductMasterRepository>()
            .AddAsync(new ProductMaster { MasterSku = "EMPTY" })).Uid;
        var empty = await _service.SyncProductsAsync(new() { ProductMasterIds = [emptySource] }, ProductMasterSyncFields.All);
        Assert.IsTrue(empty.Success, empty.Message);
        Assert.AreEqual(0, empty.Data.UpdatedProductCount);
    }

    /// <summary>无效供应商和后续 SQL 失败均回滚整批，不留下部分产品更新。</summary>
    [TestMethod]
    public async Task IndependentSync_InvalidSupplierAndSqlFailure_RollBack()
    {
        var product = await AddProductAsync(_masterId);
        await _session.Connection.ExecuteAsync("UPDATE dbo.ProductMaster SET DefaultSupplierId=@id, DefaultGrossWeight=55 WHERE Uid=@masterId",
            new { id = _supplierId, masterId = _masterId });
        await _session.Connection.ExecuteAsync("UPDATE dbo.Supplier SET IsActive=0 WHERE Uid=@id", new { id = _supplierId });
        var request = new SyncProductMasterDataRequest { ProductMasterIds = [_masterId] };
        Assert.IsFalse((await _service.SyncProductsAsync(request, ProductMasterSyncFields.All)).Success);
        Assert.AreEqual(1, (await _products.GetByIdAsync(product.Uid))!.Version);
        await _session.Connection.ExecuteAsync("UPDATE dbo.Supplier SET IsActive=1 WHERE Uid=@id", new { id = _supplierId });
        await _session.Connection.ExecuteAsync("ALTER TABLE dbo.Product ADD CONSTRAINT CK_TestIndependentSyncFailure CHECK (UnitWeight <> 55)");
        Assert.IsFalse((await _service.SyncProductsAsync(request, ProductMasterSyncFields.All)).Success);
        var saved = (await _products.GetByIdAsync(product.Uid))!;
        Assert.AreEqual("原描述", saved.DescriptionEn);
        Assert.AreEqual(1, saved.Version);
        Assert.IsNull(_session.Transaction);
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
            UPDATE dbo.ProductMaster SET DefaultDescriptionEn=N'新英文', DefaultDescriptionZh=NULL,
                DefaultFeaturesEn=N'新配置', DefaultFeaturesZh=NULL, DefaultSupplierId=@supplierId,
                DefaultGrossWeight=12.5, DefaultCbm=0.75 WHERE Uid=@masterId
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
        Assert.AreEqual("OLD", saved.MasterSku);
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
        await _session.Connection.ExecuteAsync("UPDATE dbo.ProductMaster SET IsDeleted=1 WHERE Uid=@id", new { id = deletedMasterId });
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
        await _session.Connection.ExecuteAsync("UPDATE dbo.ProductMaster SET DefaultSupplierId=@supplierId WHERE Uid=@masterId",
            new { supplierId = _supplierId, masterId = _masterId });
        await _session.Connection.ExecuteAsync("UPDATE dbo.Supplier SET IsActive=0 WHERE Uid=@id", new { id = _supplierId });
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
            ProductMasterId = masterId, MasterSku = "OLD", Sku = Guid.NewGuid().ToString("N"),
            SupplyType = "采购", IsActive = active, IsDeleted = deleted,
            DescriptionEn = "原描述", DescriptionZh = "原中文", FeaturesEn = "原配置", FeaturesZh = "原中文配置",
            DefaultSupplierId = Guid.Empty, CNYPrice = 99m
        };
        var result = await _products.AddAsync(product);
        Assert.IsTrue(result.Success, result.Message);
        if (deleted)
            await _session.Connection.ExecuteAsync("UPDATE dbo.Product SET IsDeleted=1 WHERE Uid=@Uid", new { product.Uid });
        return product;
    }

    private SaveProductMasterDetailRequest DetailRequest(bool descriptions, bool supplier) => new()
    {
        ProductMasterId = _masterId, ExpectedVersion = 1,
        UpdateProductDescriptions = descriptions, UpdateProductDefaultSupplier = supplier,
        Master = new() { MasterSku = " NEW ", DefaultDescriptionEn = " 英文描述 ", DefaultDescriptionZh = "中文描述",
            DefaultFeaturesEn = "英文配置", DefaultFeaturesZh = " ", DefaultSupplierId = _supplierId }
    };

    private static ProductMasterBatchSaveRequest BatchRequest(SaveProductMasterDetailRequest request) => new()
    {
        UpdateProductDescriptions = request.UpdateProductDescriptions,
        UpdateProductDefaultSupplier = request.UpdateProductDefaultSupplier,
        UpdateItems = [new() { Uid = request.ProductMasterId, Version = request.ExpectedVersion, Data = request.Master }]
    };

    private async Task<bool> SaveAsync(bool detail, bool descriptions, bool supplier, bool addNeutral = false)
    {
        var request = DetailRequest(descriptions, supplier);
        if (addNeutral) request.CreateNeutrals.Add(new() { Data = new() { ProductCode = "N", ColorId = _colorId } });
        return detail ? (await _service.SaveDetailAsync(request)).Success : (await _service.BatchSaveAsync(BatchRequest(request))).Success;
    }

    /// <summary>只从编译期指定的 Entity 建隔离测试表，确保仓储写入和详情投影可执行。</summary>
    private static string CreateTable(Type type)
    {
        var table = type.GetCustomAttribute<TableAttribute>()!.Name;
        var columns = type.GetProperties().Where(property => property.CanWrite && property.GetCustomAttribute<NotMappedAttribute>() is null)
            .Select(property =>
            {
                var valueType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
                var sqlType = valueType == typeof(string) ? "nvarchar(max)" : valueType == typeof(Guid) ? "uniqueidentifier"
                    : valueType == typeof(int) ? "int" : valueType == typeof(bool) ? "bit"
                    : valueType == typeof(decimal) ? "decimal(18,6)" : valueType == typeof(DateTime) ? "datetime2"
                    : valueType.IsEnum ? "int" : throw new NotSupportedException(valueType.Name);
                return $"[{property.Name}] {sqlType} {(property.Name == nameof(BaseEntity.Uid) ? "NOT NULL PRIMARY KEY" : "NULL")}";
            });
        return $"CREATE TABLE dbo.[{table}] ({string.Join(",", columns)});";
    }

    private static string ConnectionString(string database) => new SqlConnectionStringBuilder
    {
        DataSource = @"(LocalDB)\MSSQLLocalDB", InitialCatalog = database, IntegratedSecurity = true, TrustServerCertificate = true
    }.ConnectionString;

    /// <summary>固定审计身份，测试无需认证服务器。</summary>
    private sealed class TestUser : ICurrentUser
    {
        public string UserName => "master-sync-test";
        public Guid? UserId => null;
        public bool IsAuthenticated => true;
    }
}
