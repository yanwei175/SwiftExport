using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Core.Entities.Relations;

namespace SwiftExport.Application.Tests.Customers;

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

    [TestMethod]
    public void CustomerProductQueriesAndSave_ShouldNotReadOrWriteLegacyFields()
    {
        var repository = Read(
            "SwiftExport.Infrastructure",
            "Repositories",
            "Customers",
            "CustomerProducts",
            "CustomerProductRepository.cs");
        var service = Read(
            "SwiftExport.Application",
            "Services",
            "Customers",
            "CustomerProducts",
            "Maintenance",
            "CustomerProductMaintenanceService.cs");

        Assert.IsFalse(repository.Contains("CustomerSkuUnit", StringComparison.Ordinal));
        Assert.IsFalse(repository.Contains("cp.Currency", StringComparison.Ordinal));
        Assert.IsFalse(service.Contains("dto.Currency", StringComparison.Ordinal));
        StringAssert.Contains(service, "customerRepository.GetByIdListAsync(customerIds)");
        StringAssert.Contains(service, "包含不存在或已删除的客户");
    }

    [TestMethod]
    public void CurrencyDeletionGuard_ShouldNotInspectCustomerProduct()
    {
        var repository = Read(
            "SwiftExport.Infrastructure",
            "Repositories",
            "Systems",
            "Currencies",
            "SysDicCurrencyRepository.cs");

        Assert.IsFalse(repository.Contains("dbo.CustomerProduct", StringComparison.Ordinal));
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
