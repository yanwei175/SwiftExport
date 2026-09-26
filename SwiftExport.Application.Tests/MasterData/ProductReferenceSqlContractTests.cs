using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SwiftExport.Application.Tests.MasterData;

/// <summary>离线检查产品参考关联投影和入出库候选仓库边界，不连接业务数据库。</summary>
[TestClass]
public sealed class ProductReferenceSqlContractTests
{
    /// <summary>产品查询保留供应商编码实时投影，旧四列不得再成为产品 SQL 读写来源。</summary>
    [TestMethod]
    public void ProductSql_UsesSupplierJoinWithoutLegacyProductColumns()
    {
        var repository = Read("SwiftExport.Infrastructure/Repositories/Products/Catalog/ProductRepository.cs");
        var sync = Read("SwiftExport.Infrastructure/Repositories/Products/Catalog/ProductRepository.MasterSynchronization.cs");
        StringAssert.Contains(repository, "defaultSupplier.{nameof(Supplier.SupplierCode)} AS {nameof(ProductDto.DefaultSupplierCode)}");
        Assert.IsFalse(repository.Contains("SELECT p.*", StringComparison.Ordinal),
            "详情查询不能把运行库尚未清理的旧编码列一并投影到只读展示字段。");
        foreach (var field in new[] { "Product.DefaultCustomerCode", "Product.DefaultCustomerSku", "Product.DefaultSupplierCode", "Product.DefaultSupplierSku" })
        {
            Assert.IsFalse(repository.Contains(field, StringComparison.Ordinal), field);
            Assert.IsFalse(sync.Contains(field, StringComparison.Ordinal), field);
        }
        Assert.IsFalse(sync.Contains("DefaultSupplierCode", StringComparison.Ordinal));
    }

    /// <summary>未指定采购供应商时仍可选择产品，供应商型号为空；指定时只从关系档案读取。</summary>
    [TestMethod]
    public void ProcurementCandidate_UsesOnlySupplierProductSku()
    {
        var repository = Read("SwiftExport.Infrastructure/Repositories/MasterData/ProcurementMasterDataQueryRepository.cs");
        Assert.IsFalse(repository.Contains("p.DefaultSupplierSku", StringComparison.Ordinal));
        StringAssert.Contains(repository, "CAST(N'' AS nvarchar(200))");
        StringAssert.Contains(repository, "COALESCE(sp.SupplierSku,N'')");
    }

    /// <summary>产品制造商和采购供应商必须使用独立别名，仓库以左连接保留无关联产品。</summary>
    [TestMethod]
    public void ProductQueries_KeepOptionalReferencesAndDistinctSupplierRoles()
    {
        var source = Read("SwiftExport.Infrastructure/Repositories/Products/Catalog/ProductRepository.cs");
        StringAssert.Contains(source, "LEFT JOIN [{SupplierTable}] manufacturer");
        StringAssert.Contains(source, "LEFT JOIN [{SupplierTable}] defaultSupplier");
        StringAssert.Contains(source, "LEFT JOIN [{WarehouseSiteTable}] warehouse");
        StringAssert.Contains(source, "nameof(ProductDto.ManufacturerName)");
        StringAssert.Contains(source, "nameof(ProductDto.DefaultWarehouseName)");
    }

    /// <summary>参考仓库投影不能撤销出库实际仓库余额筛选，也不能变为入库来源的内连接资格。</summary>
    [TestMethod]
    public void InventoryCandidates_KeepActualWarehouseAndSourceEligibility()
    {
        var inbound = Read("SwiftExport.Infrastructure/Repositories/Inventory/Inbounds/InboundSourceRepository.cs");
        var outbound = Read("SwiftExport.Infrastructure/Repositories/Inventory/Outbounds/OutboundQueryRepository.cs");
        StringAssert.Contains(inbound, "LEFT JOIN dbo.WarehouseSite defaultWarehouse");
        StringAssert.Contains(inbound, "INNER JOIN dbo.Product product ON product.Uid=l.ProductId");
        StringAssert.Contains(outbound, "LEFT JOIN dbo.WarehouseSite defaultWarehouse");
        StringAssert.Contains(outbound, "b.WarehouseSiteId=@WarehouseSiteId AND b.IsDeleted=0 AND b.OnHandQty>0");
    }

    private static string Read(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SwiftExport.slnx")))
            directory = directory.Parent;
        Assert.IsNotNull(directory);
        return File.ReadAllText(Path.Combine(directory.FullName, relativePath));
    }
}
