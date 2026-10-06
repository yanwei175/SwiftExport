using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Core.Entities.Relations;

namespace SwiftExport.Application.Tests.Customers;

/// <summary>保留客户产品旧字段退出及币种引用归属的有效契约，随当前 PG 仓储核对删除保护。</summary>
[TestClass]
public sealed class CustomerProductLegacyFieldRetirementContractTests
{
    [TestMethod]
    public void CustomerProductModelAndContract_ShouldNotExposeUnitOrCurrency()
    {
        Assert.IsNull(typeof(CustomerProduct).GetProperty("CustomerSkuUnit"));
        Assert.IsNull(typeof(CustomerProduct).GetProperty("Currency"));
        Assert.IsNull(typeof(CustomerProductDto).GetProperty("CustomerSkuUnit"));
        Assert.IsNull(typeof(CustomerProductDto).GetProperty("Currency"));
    }

    /// <summary>当前 PG 查询不读取客户单位/币种旧列，普通保存仍校验客户身份，不接收客户产品币种。</summary>
    [TestMethod]
    public void CustomerProductQueriesAndSave_ShouldNotReadOrWriteLegacyFields()
    {
        var repository = Read(
            "SwiftExport.Infrastructure",
            "Repositories",
            "Customers",
            "CustomerProducts",
            "PostgreSqlCustomerProductRepository.cs");
        var service = Read(
            "SwiftExport.Application",
            "Services",
            "Customers",
            "CustomerProducts",
            "Maintenance",
            "CustomerProductMaintenanceService.cs");

        Assert.IsFalse(repository.Contains("CustomerSkuUnit", StringComparison.Ordinal));
        Assert.IsFalse(repository.Contains("customer_sku_unit", StringComparison.Ordinal));
        Assert.IsFalse(repository.Contains("cp.currency", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(repository.Contains("cp.\"currency\"", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(service.Contains("dto.Currency", StringComparison.Ordinal));
        StringAssert.Contains(service, "customerRepository.GetByIdListAsync(customerIds)");
        StringAssert.Contains(service, "包含不存在或已删除的客户");
    }

    /// <summary>PG 币种删除保护不能从客户产品取币种，兼顾直接 SQL 和元数据生成引用。</summary>
    [TestMethod]
    public void CurrencyDeletionGuard_ShouldNotInspectCustomerProduct()
    {
        var repository = Read(
            "SwiftExport.Infrastructure",
            "Repositories",
            "Systems",
            "Currencies",
            "PostgreSqlSysDicCurrencyRepository.cs");

        Assert.IsFalse(repository.Contains("public.customer_product", StringComparison.Ordinal));
        Assert.IsFalse(repository.Contains("typeof(CustomerProduct)", StringComparison.Ordinal));
    }

    private static string Read(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { Root() }.Concat(parts).ToArray()));

    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SwiftExport-WebAPI.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        Assert.Fail("无法定位 SwiftExport 仓库根目录。");
        return string.Empty;
    }
}
