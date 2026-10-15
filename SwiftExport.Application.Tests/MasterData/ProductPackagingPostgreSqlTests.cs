using Dapper;
using Mapster;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Reflection;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Services.Products.Catalog.Maintenance;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces.Products.Catalog;
using SwiftExport.Core.Interfaces.Products.Attributes;
using SwiftExport.Core.Interfaces.Products.ProductMasters;
using SwiftExport.Core.Interfaces.Products.Colors;
using SwiftExport.Core.Interfaces.Suppliers.Management;
using SwiftExport.Core.Interfaces.Inventory.Warehouses;
using SwiftExport.Core.Interfaces.Systems.Dictionaries.Data;
using SwiftExport.Core.Interfaces;
using SwiftExport.Infrastructure.Repositories.Base;
using SwiftExport.Infrastructure.Repositories.Shipping;
using SwiftExport.Infrastructure.Contexts;

namespace SwiftExport.Application.Tests.MasterData;

/// <summary>获批隔离库的真实 Application/PG 连通验收；不经过 HTTP，不冒称真实 API 或人工客户端操作。</summary>
[TestClass, DoNotParallelize, TestCategory("PostgreSqlPackagingAcceptance")]
public sealed partial class ProductPackagingPostgreSqlTests
{
    /// <summary>真实新建包材、物料、多层包装并保存再读，三入口仅采用 Carton，原非标与 SO/CR 条件保持。</summary>
    [TestMethod]
    [DataRow("present", false, "SO")]
    [DataRow("missing", false, "SO")]
    [DataRow("deleted", false, "SO")]
    [DataRow("present", true, "SO")]
    [DataRow("present", false, "CR")]
    [DataRow("present", true, "CR")]
    public async Task EndToEnd_ThreeEntrancesUseOnlyCurrentCarton(string cartonState, bool nonStandard, string sourceType)
    {
        await using var fixture = await ProductPackagingPostgreSqlFixture.CreateAsync();
        var owner = await fixture.AddProductAsync(); var material = await fixture.AddProductAsync(true);
        using var scope = fixture.NewScope();
        var materialDto = (await scope.Repository.GetDtoByIdAsync(material.Uid))!;
        materialDto.UnitWeight = 0.25m; materialDto.UpdateUnitWeight = true;
        var weight = await scope.Maintenance.UpdateAsync(new() { Uid = material.Uid, Version = material.Version, Data = materialDto });
        Assert.IsTrue(weight.Success, weight.Message);
        var lookup = await scope.Repository.SearchLookupAsync(new ProductLookupRequest { Keyword = material.Sku, MaterialType = MaterialTypesEnum.包材 });
        Assert.AreEqual(material.Uid, lookup.Single().Uid);
        var read = await scope.Query.GetDetailAsync(owner.Uid); Assert.IsTrue(read.Success, read.Message);
        var request = Request(read.Data, new()
        {
            CreateItems = [Create(ProductPackageTypeCodes.InnerBox, 999, material.Uid), Create(ProductPackageTypeCodes.ShippingBox, 1), Create(ProductPackageTypeCodes.Pallet, 9999)]
        });
        if (cartonState != "missing") request.PackagingChanges.CreateItems.Add(Create(ProductPackageTypeCodes.Carton, 12, material.Uid));
        var saved = await scope.Maintenance.SaveDetailAsync(request); Assert.IsTrue(saved.Success, saved.Message);
        if (cartonState == "deleted")
        {
            var carton = saved.Data.Detail.Packages.Single(row => row.Data.PackageType == ProductPackageTypeCodes.Carton);
            saved = await scope.Maintenance.SaveDetailAsync(Request(saved.Data.Detail, new() { DeleteItems = [new() { Uid = carton.Uid, Version = carton.Version }] }));
            Assert.IsTrue(saved.Success, saved.Message);
        }
        var detail = (await scope.Query.GetDetailAsync(owner.Uid)).Data;
        var page = (await scope.Repository.GetPagedAsync(new() { Keyword = owner.Sku })).Items.Single();
        var source = await fixture.AddSourceAsync(owner.Uid, nonStandard, sourceType);
        var packing = (await new PostgreSqlPackingListRepository(scope.Services.GetRequiredService<RepositoryBaseContext>()).GetSourceEnrichmentsAsync([source.Uid])).Single();
        var exists = cartonState == "present";
        Assert.AreEqual(exists ? 12m : (decimal?)null, detail.PcsOfCarton); Assert.AreEqual(detail.PcsOfCarton, page.PcsOfCarton);
        Assert.AreEqual(exists ? 10m : (decimal?)null, detail.NwOfCarton); Assert.AreEqual(detail.NwOfCarton, page.NwOfCarton);
        Assert.AreEqual(exists ? 11m : (decimal?)null, detail.GwOfCarton); Assert.AreEqual(detail.GwOfCarton, page.GwOfCarton);
        Assert.AreEqual(exists ? 0.4m : (decimal?)null, detail.CbmOfCarton); Assert.AreEqual(detail.CbmOfCarton, page.CbmOfCarton);
        Assert.AreEqual(exists && !nonStandard ? detail.PcsOfCarton : null, packing.PcsPerPackage);
        Assert.AreEqual(exists && !nonStandard ? detail.NwOfCarton : null, packing.NwPerPackage);
        Assert.AreEqual(exists && !nonStandard ? detail.GwOfCarton : null, packing.GwPerPackage);
        Assert.AreEqual(exists && !nonStandard ? detail.CbmOfCarton : null, packing.CbmPerPackage);
        Assert.AreEqual(0.25m, (await scope.Repository.GetByIdAsync(material.Uid))!.UnitWeight);
        Assert.IsNull(packing.CustomerPoNo); Assert.IsNull(packing.InvoiceNo);
    }

    /// <summary>父/行版本及跨 Owner 校验基于真实已提交行；无效后序资料使先前包装写入真实回滚。</summary>
    [TestMethod]
    [DataRow("owner")] [DataRow("row-version")] [DataRow("parent-version")] [DataRow("late-validation")]
    public async Task VersionOwnerAndLateValidation_DoNotLeavePartialWrites(string scenario)
    {
        await using var fixture = await ProductPackagingPostgreSqlFixture.CreateAsync();
        var owner = await fixture.AddProductAsync(); var other = await fixture.AddProductAsync();
        using var scope = fixture.NewScope(); var initial = await AddCarton(scope, owner.Uid);
        var row = initial.Packages.Single();
        var detail = scenario == "owner" ? (await scope.Query.GetDetailAsync(other.Uid)).Data : initial;
        var changes = new ProductPackageChangesRequest();
        if (scenario == "late-validation") changes.CreateItems.Add(Create(ProductPackageTypeCodes.InnerBox, 2));
        else changes.UpdateItems.Add(new() { Uid = row.Uid, Version = scenario == "row-version" ? row.Version + 1 : row.Version, Data = new() { PackageType = ProductPackageTypeCodes.Carton, PcsPerPackage = 99 } });
        var request = Request(detail, changes); if (scenario == "parent-version") request.ExpectedVersion--;
        if (scenario == "late-validation") request.Data.Sku = "";
        var result = await scope.Maintenance.SaveDetailAsync(request); Assert.IsFalse(result.Success);
        Assert.IsNull(scope.Session.Transaction);
        var unchanged = (await scope.Query.GetDetailAsync(owner.Uid)).Data;
        Assert.AreEqual(initial.Version, unchanged.Version); Assert.HasCount(1, unchanged.Packages);
        Assert.AreEqual(row.Version, unchanged.Packages.Single().Version); Assert.AreEqual(12, unchanged.Packages.Single().Data.PcsPerPackage);
        Assert.HasCount(0, (await scope.Query.GetDetailAsync(other.Uid)).Data.Packages);
    }

    /// <summary>真实物料和包装已写入后注入读回故障，外层事务同时撤销父行和包装新行。</summary>
    [TestMethod]
    public async Task FailureAfterProductAndPackageWrites_RollsBackActualDatabase()
    {
        await using var fixture = await ProductPackagingPostgreSqlFixture.CreateAsync(); var owner = await fixture.AddProductAsync();
        using var scope = fixture.NewScope(); var initial = await AddCarton(scope, owner.Uid);
        var proxy = DispatchProxy.Create<IProductRepository, ReadBackFailureProxy>(); ((ReadBackFailureProxy)proxy).Inner = scope.Repository;
        var services = scope.Services;
        var failing = new ProductMaintenanceService(proxy, services.GetRequiredService<ServiceBaseContext>(),
            services.GetRequiredService<IProductColorRepository>(), services.GetRequiredService<ISupplierRepository>(), services.GetRequiredService<IWarehouseQueryRepository>(),
            services.GetRequiredService<IBomRepository>(), services.GetRequiredService<ISysDictDataRepository>(), services.GetRequiredService<IProductAttributeRepository>(), services.GetRequiredService<IProductMasterRepository>());
        var request = Request(initial, new() { CreateItems = [Create(ProductPackageTypeCodes.InnerBox, 2)] }); request.Data.Note = "不得提交的父行修改";
        var result = await failing.SaveDetailAsync(request); Assert.IsFalse(result.Success); Assert.IsNull(scope.Session.Transaction);
        var after = (await scope.Query.GetDetailAsync(owner.Uid)).Data;
        Assert.AreEqual(initial.Version, after.Version); Assert.AreEqual(initial.Data.Note, after.Data.Note); Assert.HasCount(1, after.Packages);
        CollectionAssert.AreEqual(initial.SelectedOptionIds, after.SelectedOptionIds);
    }

    /// <summary>真实唯一索引允许事务内交换类型，删除后重建同类型；暂存标识不得残留到已提交行。</summary>
    [TestMethod]
    public async Task TypeSwapAndDeleteRecreate_KeepUniqueRolesAndVersions()
    {
        await using var fixture = await ProductPackagingPostgreSqlFixture.CreateAsync(); var owner = await fixture.AddProductAsync();
        using var scope = fixture.NewScope(); var read = (await scope.Query.GetDetailAsync(owner.Uid)).Data;
        var saved = await scope.Maintenance.SaveDetailAsync(Request(read, new() { CreateItems = [Create(ProductPackageTypeCodes.Carton, 12), Create(ProductPackageTypeCodes.InnerBox, 2)] }));
        Assert.IsTrue(saved.Success, saved.Message);
        var carton = saved.Data.Detail.Packages.Single(row => row.Data.PackageType == ProductPackageTypeCodes.Carton);
        var inner = saved.Data.Detail.Packages.Single(row => row.Data.PackageType == ProductPackageTypeCodes.InnerBox);
        saved = await scope.Maintenance.SaveDetailAsync(Request(saved.Data.Detail, new()
        {
            UpdateItems = [new() { Uid = carton.Uid, Version = carton.Version, Data = new() { PackageType = ProductPackageTypeCodes.InnerBox, PcsPerPackage = 12 } },
                new() { Uid = inner.Uid, Version = inner.Version, Data = new() { PackageType = ProductPackageTypeCodes.Carton, PcsPerPackage = 2 } }]
        }));
        Assert.IsTrue(saved.Success, saved.Message); Assert.AreEqual(2m, saved.Data.Detail.PcsOfCarton);
        Assert.AreEqual(2, saved.Data.Detail.Packages.Single(row => row.Uid == inner.Uid).Version);
        saved = await scope.Maintenance.SaveDetailAsync(Request(saved.Data.Detail, new()
        { DeleteItems = [new() { Uid = inner.Uid, Version = 2 }], CreateItems = [Create(ProductPackageTypeCodes.Carton, 24)] }));
        Assert.IsTrue(saved.Success, saved.Message); Assert.AreEqual(24m, saved.Data.Detail.PcsOfCarton);
        Assert.AreNotEqual(inner.Uid, saved.Data.Detail.Packages.Single(row => row.Data.PackageType == ProductPackageTypeCodes.Carton).Uid);
        Assert.AreEqual(0, await scope.Session.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.product_package WHERE owner_product_id=@Owner AND package_type LIKE '__editing__%'", new { Owner = owner.Uid }));
    }

    /// <summary>模拟既有缺失引用，左连接保留完整包装行；显式清除后仍保留装率和关系身份。</summary>
    [TestMethod]
    public async Task BrokenMaterialReference_IsVisibleAndCanBeCleared()
    {
        await using var fixture = await ProductPackagingPostgreSqlFixture.CreateAsync(); var owner = await fixture.AddProductAsync();
        using var scope = fixture.NewScope();
        using (var transaction = scope.Session.Connection.BeginTransaction())
        {
            scope.Session.Transaction = transaction;
            await scope.Repository.LockProductRowsAsync([owner.Uid]);
            var seeded = await scope.Repository.AddPackageAsync(new ProductPackage { OwnerProductId = owner.Uid, PackagingMaterialId = Guid.NewGuid(), PcsPerPackage = 12 });
            Assert.IsTrue(seeded.Success, seeded.Message); transaction.Commit(); scope.Session.Transaction = null;
        }
        var detail = (await scope.Query.GetDetailAsync(owner.Uid)).Data; var broken = detail.Packages.Single();
        Assert.IsFalse(broken.IsPackagingMaterialValid); Assert.IsNull(broken.PackagingMaterialSku); Assert.IsNotNull(broken.Data.PackagingMaterialId);
        var data = broken.Data.Adapt<ProductPackageWriteDto>(); data.PackagingMaterialId = null;
        var saved = await scope.Maintenance.SaveDetailAsync(Request(detail, new() { UpdateItems = [new() { Uid = broken.Uid, Version = broken.Version, Data = data }] }));
        Assert.IsTrue(saved.Success, saved.Message); Assert.AreEqual(broken.Uid, saved.Data.Detail.Packages.Single().Uid);
        Assert.IsTrue(saved.Data.Detail.Packages.Single().IsPackagingMaterialValid); Assert.IsNull(saved.Data.Detail.Packages.Single().Data.PackagingMaterialId);
    }

    /// <summary>创建最小包装命令，故意让手工 CBM 不等于尺寸乘积，以验证没有自动换算。</summary>
    private static CreateItemRequest<ProductPackageWriteDto> Create(string type, int pcs, Guid? material = null) => new()
    { Data = new() { PackageType = type, PcsPerPackage = pcs, PackagingMaterialId = material, Length_CM = 20, Width_CM = 30, Height_CM = 40, Nw_KG = 10, Gw_KG = 11, Cbm = 0.4m } };
    /// <summary>从真实读快照冻结写请求，不共享可变资料对象。</summary>
    private static SaveProductDetailRequest Request(ProductDetailDto detail, ProductPackageChangesRequest? changes) => new()
    { ProductId = detail.Uid, ExpectedVersion = detail.Version, Data = detail.Data.Adapt<ProductDetailWriteDto>(), PackagingChanges = changes };
    private static async Task<ProductDetailDto> AddCarton(ProductPackagingPostgreSqlFixture.Scope scope, Guid owner, Guid? material = null)
    {
        var read = await scope.Query.GetDetailAsync(owner); Assert.IsTrue(read.Success, read.Message);
        var saved = await scope.Maintenance.SaveDetailAsync(Request(read.Data, new() { CreateItems = [Create(ProductPackageTypeCodes.Carton, 12, material)] }));
        Assert.IsTrue(saved.Success, saved.Message); return saved.Data.Detail;
    }
    /// <summary>只在真实写入后的读回边界注入失败，其余仓储调用仍落在相同真实 PG 会话。</summary>
    public class ReadBackFailureProxy : DispatchProxy
    {
        public IProductRepository Inner { get; set; } = null!;
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        { if (method!.Name == nameof(IProductRepository.GetDetailAsync)) throw new InvalidOperationException("测试：写入后读回失败"); return method.Invoke(Inner, args); }
    }
}
