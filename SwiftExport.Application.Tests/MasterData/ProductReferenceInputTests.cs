using System.Reflection;
using Mapster;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Services.Products.Catalog.Maintenance;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Core.Entities.Inventory;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces;
using SwiftExport.Core.Interfaces.Inventory.Warehouses;
using SwiftExport.Core.Interfaces.Products.Catalog;
using SwiftExport.Core.Interfaces.Products.Colors;
using SwiftExport.Core.Interfaces.Products.ProductMasters;
using SwiftExport.Core.Interfaces.Suppliers.Management;
using SwiftExport.Core.Interfaces.Systems.Dictionaries.Data;

namespace SwiftExport.Application.Tests.MasterData;

/// <summary>验证产品可选仓库、制造商和型号在保存前的纯输入规范化与拒绝边界。</summary>
[TestClass]
public sealed class ProductReferenceInputTests
{
    /// <summary>型号允许独立保存，空白归空且不截断。</summary>
    [TestMethod]
    public void ManufacturerModel_IsIndependentAndNormalized()
    {
        var dto = new ProductDto { Sku = "P-1", ManufacturerModel = "  M-1  " };
        Assert.IsNull(Validate(dto));
        Assert.AreEqual("M-1", dto.ManufacturerModel);

        dto.ManufacturerModel = "   ";
        Assert.IsNull(Validate(dto));
        Assert.IsNull(dto.ManufacturerModel);
    }

    /// <summary>空 Guid 和超长型号是输入错误，不能写作有效逻辑关联。</summary>
    [TestMethod]
    public void EmptyIdentifiersAndOversizedModel_AreRejected()
    {
        Assert.IsNotNull(Validate(new ProductDto { Sku = "P-1", ManufacturerId = Guid.Empty }));
        Assert.IsNotNull(Validate(new ProductDto { Sku = "P-1", DefaultWarehouseSiteId = Guid.Empty }));
        Assert.IsNotNull(Validate(new ProductDto { Sku = "P-1", ManufacturerModel = new string('M', 201) }));
        Assert.IsNull(Validate(new ProductDto { Sku = "P-1", ManufacturerModel = new string('M', 200) }));
    }

    /// <summary>标准单条更新的实体适配必须把显式 null 写入旧关联，不能保留旧 ID。</summary>
    [TestMethod]
    public void ProductDtoAdapt_ShouldClearOptionalReferences()
    {
        var entity = new Product
        {
            Sku = "P-1", DefaultWarehouseSiteId = Guid.NewGuid(),
            ManufacturerId = Guid.NewGuid(), ManufacturerModel = "旧型号"
        };
        new ProductDto { Sku = "P-1", ManufacturerModel = "新型号" }.Adapt(entity);
        Assert.IsNull(entity.DefaultWarehouseSiteId);
        Assert.IsNull(entity.ManufacturerId);
        Assert.AreEqual("新型号", entity.ManufacturerModel);
    }

    /// <summary>未改动的停用关联仍可保留并保存产品的其他字段。</summary>
    [TestMethod]
    public async Task UnchangedInactiveReferences_CanBePreserved()
    {
        var manufacturerId = Guid.NewGuid();
        var warehouseId = Guid.NewGuid();
        var existing = new Product { Sku = "P-1", ManufacturerId = manufacturerId, DefaultWarehouseSiteId = warehouseId };
        var data = new ProductDto { Sku = "P-1", ManufacturerId = manufacturerId, DefaultWarehouseSiteId = warehouseId };

        var error = await ValidateReferencesAsync(data, existing,
            [new Supplier { Uid = manufacturerId, SupplierCode = "M-1", SupplierName = "制造商", DocRelateNo = "", IsActive = false }],
            [new WarehouseSite { Uid = warehouseId, IsActive = false }]);

        Assert.IsNull(error);
    }

    /// <summary>未改动但已删除或不存在的关联必须由维护人清除或重选。</summary>
    [TestMethod]
    public async Task UnchangedMissingReferences_RequireClearOrReselect()
    {
        var manufacturerId = Guid.NewGuid();
        var warehouseId = Guid.NewGuid();
        var existing = new Product { Sku = "P-1", ManufacturerId = manufacturerId, DefaultWarehouseSiteId = warehouseId };

        var manufacturerError = await ValidateReferencesAsync(
            new ProductDto { Sku = "P-1", ManufacturerId = manufacturerId }, existing, [], []);
        var warehouseError = await ValidateReferencesAsync(
            new ProductDto { Sku = "P-1", DefaultWarehouseSiteId = warehouseId }, existing, [], []);

        StringAssert.Contains(manufacturerError, "清除或重新选择");
        StringAssert.Contains(warehouseError, "清除或重新选择");
    }

    /// <summary>产品主动清除已失效的两个引用后，应能继续保存其他资料。</summary>
    [TestMethod]
    public async Task MissingReferences_CanBeCleared()
    {
        var existing = new Product
        {
            Sku = "P-1", ManufacturerId = Guid.NewGuid(), DefaultWarehouseSiteId = Guid.NewGuid()
        };

        var error = await ValidateReferencesAsync(new ProductDto { Sku = "P-1" }, existing, [], []);

        Assert.IsNull(error);
    }

    /// <summary>新选或改选只能指向仍存在且启用的制造商和参考仓库。</summary>
    [TestMethod]
    public async Task ChangedReferences_RequireActiveTargets()
    {
        var manufacturerId = Guid.NewGuid();
        var warehouseId = Guid.NewGuid();
        var existing = new Product { Sku = "P-1" };
        var data = new ProductDto { Sku = "P-1", ManufacturerId = manufacturerId, DefaultWarehouseSiteId = warehouseId };
        var manufacturer = new Supplier { Uid = manufacturerId, SupplierCode = "M-1", SupplierName = "制造商", DocRelateNo = "", IsActive = false };
        var warehouse = new WarehouseSite { Uid = warehouseId, IsActive = false };

        var manufacturerError = await ValidateReferencesAsync(data, existing, [manufacturer], [warehouse]);
        manufacturer.IsActive = true;
        var warehouseError = await ValidateReferencesAsync(data, existing, [manufacturer], [warehouse]);
        warehouse.IsActive = true;
        var valid = await ValidateReferencesAsync(data, existing, [manufacturer], [warehouse]);

        StringAssert.Contains(manufacturerError, "已停用");
        StringAssert.Contains(warehouseError, "已停用");
        Assert.IsNull(valid);
    }

    private static async Task<string?> ValidateReferencesAsync(
        ProductDto data, Product? existing, Supplier[] suppliers, WarehouseSite[] warehouses)
    {
        var supplierRepository = DispatchProxy.Create<ISupplierRepository, SupplierProxy>();
        ((SupplierProxy)supplierRepository).Items = suppliers;
        var warehouseRepository = DispatchProxy.Create<IWarehouseQueryRepository, WarehouseProxy>();
        ((WarehouseProxy)warehouseRepository).Items = warehouses;
        var service = new ProductMaintenanceService(
            DispatchProxy.Create<IProductRepository, RejectProxy>(),
            new ServiceBaseContext(null!, NullLoggerFactory.Instance),
            DispatchProxy.Create<IProductColorRepository, RejectProxy>(), supplierRepository,
            warehouseRepository, DispatchProxy.Create<IBomRepository, RejectProxy>(),
            DispatchProxy.Create<ISysDictDataRepository, RejectProxy>(), new EmptyProductAttributeRepository(),
            DispatchProxy.Create<IProductMasterRepository, RejectProxy>());
        var method = typeof(ProductMaintenanceService).GetMethod("ValidateMasterDataReferencesAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        return await (Task<string?>)method.Invoke(service,
            [new[] { (data, existing) }])!;
    }

    /// <summary>保证关联校验不触碰无关仓储的替身。</summary>
    public class RejectProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args) =>
            throw new AssertFailedException($"不应调用 {method?.Name}。");
    }

    /// <summary>按已存在供应商返回档案，模拟仓储排除软删除项的行为。</summary>
    public class SupplierProxy : DispatchProxy
    {
        public Supplier[] Items { get; set; } = [];
        protected override object? Invoke(MethodInfo? method, object?[]? args) =>
            method?.Name == nameof(ISupplierRepository.GetByIdListAsync)
                ? Task.FromResult<IReadOnlyList<Supplier>>(Items)
                : throw new AssertFailedException($"不应调用 {method?.Name}。");
    }

    /// <summary>按已存在仓库返回档案，模拟仓储排除软删除项的行为。</summary>
    public class WarehouseProxy : DispatchProxy
    {
        public WarehouseSite[] Items { get; set; } = [];
        protected override object? Invoke(MethodInfo? method, object?[]? args) =>
            method?.Name == nameof(IWarehouseQueryRepository.GetByIdsAsync)
                ? Task.FromResult<IReadOnlyList<WarehouseSite>>(Items)
                : throw new AssertFailedException($"不应调用 {method?.Name}。");
    }

    private static string? Validate(ProductDto dto)
    {
        var method = typeof(ProductMaintenanceService).GetMethod("ValidateProduct", BindingFlags.NonPublic | BindingFlags.Static)!;
        return (string?)method.Invoke(null, [dto, 1, "保存"]);
    }
}
