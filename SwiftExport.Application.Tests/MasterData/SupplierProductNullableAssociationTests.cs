using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Services.Suppliers.SupplierProducts.Maintenance;
using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExport.Core.Entities.Relations;
using SwiftExport.Infrastructure.DatabaseSchema;

namespace SwiftExport.Application.Tests.MasterData;

/// <summary>验证供应商资料独立建档及可选 Product 关联的输入和离线结构。</summary>
[TestClass]
public sealed class SupplierProductNullableAssociationTests
{
    /// <summary>不选择 Product 时仍可保存供应商自有编码、描述和价格。</summary>
    [TestMethod]
    public void Validate_AllowsMissingProductButRequiresSupplierAndOwnSku()
    {
        var dto = new SupplierProductDto
        {
            SupplierId = Guid.NewGuid(), SupplierCode = "SUP-1", SupplierSku = "OWN-1",
            SupplierSkuDescriptionZh = "供应商描述", UnitPrice = 12m, SkuId = null
        };
        Assert.IsNull(SupplierProductMaintenanceService.ValidateSupplierProduct(dto, 1));
        Assert.AreEqual("OWN-1", dto.SupplierSku);
        dto.SupplierId = Guid.Empty;
        StringAssert.Contains(SupplierProductMaintenanceService.ValidateSupplierProduct(dto, 1)!, "必须关联供应商");
        dto.SupplierId = Guid.NewGuid();
        dto.SkuId = Guid.Empty;
        StringAssert.Contains(SupplierProductMaintenanceService.ValidateSupplierProduct(dto, 1)!, "标准 SKU 标识无效");
        dto.SkuId = null;
        dto.SupplierSku = " ";
        StringAssert.Contains(SupplierProductMaintenanceService.ValidateSupplierProduct(dto, 1)!, "供应商料号不能为空");
    }

    /// <summary>生成结构允许无 Product 关联资料且唯一索引不限制空关联条数。</summary>
    [TestMethod]
    public void GeneratedSchema_UsesNullableProductIdAndFilteredUniqueness()
    {
        Assert.AreEqual(typeof(Guid?), typeof(SupplierProduct).GetProperty(nameof(SupplierProduct.SkuId))!.PropertyType);
        var sql = new SqlServerSchemaGenerator().Generate(typeof(SupplierProduct).Assembly);
        StringAssert.Contains(sql, "[SkuId] uniqueidentifier NULL");
        StringAssert.Contains(sql, "[SupplierId], [SkuId]) WHERE [IsDeleted] = 0 AND [SkuId] IS NOT NULL");
    }

    /// <summary>普通列表和详情共用可选 Product 连接，未关联行不被 WHERE 排除。</summary>
    [TestMethod]
    public void QuerySql_KeepsUnlinkedSupplierProductVisible()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
            directory = directory.Parent;
        Assert.IsNotNull(directory);
        var path = Path.Combine(directory.FullName, "SwiftExport.Infrastructure", "Repositories", "Suppliers", "SupplierProducts", "SupplierProductRepository.cs");
        var sql = File.ReadAllText(path);
        StringAssert.Contains(sql, "LEFT JOIN dbo.Product p WITH (NOLOCK)");
        StringAssert.Contains(sql, "ON p.Uid = sp.SkuId AND ISNULL(p.IsDeleted, 0) = 0");
        StringAssert.Contains(sql, "sp.SupplierSku LIKE @Keyword");
        Assert.IsFalse(sql.Contains("WHERE p.Uid IS NOT NULL", StringComparison.OrdinalIgnoreCase));
    }
}
