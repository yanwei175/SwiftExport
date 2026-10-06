using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Infrastructure.Repositories.Products.Catalog;
using SwiftExport.Infrastructure.Repositories.Inventory.Outbounds;

namespace SwiftExport.Application.Tests.MasterData;

/// <summary>离线检查产品参考关联投影和入出库候选仓库边界，不连接业务数据库。</summary>
[TestClass]
public sealed class ProductReferenceSqlContractTests
{
    /// <summary>产品查询保留供应商编码实时投影，旧四列不得再成为产品 SQL 读写来源。</summary>
    [TestMethod]
    public void ProductSql_UsesSupplierJoinWithoutLegacyProductColumns()
    {
        var repository = Read("SwiftExport.Infrastructure/Repositories/Products/Catalog/PostgreSqlProductRepository.Query.cs");
        var sync = Read("SwiftExport.Infrastructure/Repositories/Products/Catalog/PostgreSqlProductRepository.MasterSynchronization.cs");
        StringAssert.Contains(ProductField("CatalogReferenceColumns"), "defaultSupplier.supplier_code AS \"DefaultSupplierCode\"");
        Assert.IsFalse(repository.Contains("SELECT p.*", StringComparison.Ordinal),
            "详情查询不能把运行库尚未清理的旧编码列一并投影到只读展示字段。");
        foreach (var field in new[] { "Product.DefaultCustomerCode", "Product.DefaultCustomerSku", "Product.DefaultSupplierCode", "Product.DefaultSupplierSku" })
        {
            Assert.IsFalse(repository.Contains(field, StringComparison.Ordinal), field);
            Assert.IsFalse(sync.Contains(field, StringComparison.Ordinal), field);
        }
        Assert.IsFalse(sync.Contains("DefaultSupplierCode", StringComparison.Ordinal));
        var productColumns = ProductField("CatalogEntityColumns") + ProductField("CatalogDtoColumns");
        foreach (var field in new[] { "default_customer_code", "default_customer_sku", "default_supplier_code", "default_supplier_sku" })
        {
            Assert.IsFalse(productColumns.Contains("p.\"" + field + "\"", StringComparison.Ordinal), field);
            Assert.IsFalse(sync.Contains("p." + field, StringComparison.Ordinal), field);
        }
    }

    /// <summary>未指定采购供应商时仍可选择产品，供应商型号为空；指定时只从关系档案读取。</summary>
    [TestMethod]
    public void ProcurementCandidate_UsesOnlySupplierProductSku()
    {
        var repository = Read("SwiftExport.Infrastructure/Repositories/MasterData/PostgreSqlProcurementMasterDataQueryRepository.cs");
        Assert.IsFalse(repository.Contains("p.DefaultSupplierSku", StringComparison.Ordinal));
        Assert.IsFalse(repository.Contains("p.default_supplier_sku", StringComparison.Ordinal));
        StringAssert.Contains(repository, "hasSupplier ? \"COALESCE(sp.supplier_sku,'')\" : \"CAST('' AS text)\"");
    }

    /// <summary>产品制造商和采购供应商必须使用独立别名，仓库以左连接保留无关联产品。</summary>
    [TestMethod]
    public void ProductQueries_KeepOptionalReferencesAndDistinctSupplierRoles()
    {
        var joins = ProductField("CatalogReferenceJoins") + ProductField("CatalogQueryJoins");
        StringAssert.Contains(joins, "LEFT JOIN public.supplier manufacturer ON manufacturer.uid=p.manufacturer_id");
        StringAssert.Contains(joins, "LEFT JOIN public.supplier defaultSupplier ON defaultSupplier.uid=p.default_supplier_id");
        StringAssert.Contains(joins, "LEFT JOIN public.warehouse_site warehouse ON warehouse.uid=p.default_warehouse_site_id");
        StringAssert.Contains(ProductField("CatalogReferenceColumns"), "manufacturer.supplier_name AS \"ManufacturerName\"");
        StringAssert.Contains(ProductField("CatalogReferenceColumns"), "warehouse.name AS \"DefaultWarehouseName\"");
    }

    /// <summary>参考仓库投影不能撤销出库实际仓库余额筛选，也不能变为入库来源的内连接资格。</summary>
    [TestMethod]
    public void InventoryCandidates_KeepActualWarehouseAndSourceEligibility()
    {
        var inbound = Read("SwiftExport.Infrastructure/Repositories/Inventory/Inbounds/PostgreSqlInboundSourceRepository.cs");
        var outbound = (string)typeof(PostgreSqlOutboundQueryRepository).GetMethod("ItemsSql", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.Invoke(null, null)!;
        StringAssert.Contains(inbound, "LEFT JOIN public.warehouse_site default_warehouse");
        StringAssert.Contains(inbound, "JOIN public.product product ON product.uid=l.product_id");
        StringAssert.Contains(outbound, "LEFT JOIN \"public\".\"warehouse_site\" dw");
        StringAssert.Contains(outbound, "b.\"warehouse_site_id\"=@WarehouseSiteId AND b.\"is_deleted\"=FALSE");
        StringAssert.Contains(outbound, "b.\"on_hand_qty\">0");
    }

    /// <summary>读取 PG 产品实际使用的固定投影或连接片段，不连接数据库。</summary>
    private static string ProductField(string name)
        => (string)typeof(PostgreSqlProductRepository).GetField(name, System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.GetValue(null)!;

    private static string Read(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SwiftExport.slnx")))
            directory = directory.Parent;
        Assert.IsNotNull(directory);
        return File.ReadAllText(Path.Combine(directory.FullName, relativePath));
    }
}
