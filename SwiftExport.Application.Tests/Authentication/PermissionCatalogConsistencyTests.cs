using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.Auth;

namespace SwiftExport.Application.Tests.Authentication;

/// <summary>约束权限常量和目录一一对应，防止授权字符串在整理后分叉。</summary>
[TestClass]
public sealed class PermissionCatalogConsistencyTests
{
    /// <summary>每个公开权限常量恰好登记一次，目录不包含常量集合外的键。</summary>
    [TestMethod]
    public void ConstantsAndCatalogHaveIdenticalUniqueKeys()
    {
        var keys = typeof(PermissionKeys).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!).ToArray();
        var catalog = PermissionCatalog.All.Select(item => item.PermissionKey).ToArray();
        Assert.AreEqual(keys.Length, keys.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.AreEqual(catalog.Length, catalog.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        CollectionAssert.AreEquivalent(keys, catalog);
    }

    /// <summary>销售六项权限统一使用 Sales.Order，旧权限前缀不再被目录接受。</summary>
    [TestMethod]
    public void SalesPermissionsUseCanonicalPrefix()
    {
        string[] expected = ["Sales.Order.View", "Sales.Order.Maintain", "Sales.Order.Delete",
            "Sales.Order.Confirm", "Sales.Order.CancelConfirmation", "Sales.Order.Lifecycle"];
        CollectionAssert.AreEquivalent(expected,
            PermissionCatalog.All.Where(item => item.GroupKey == "Sales")
                .Select(item => item.PermissionKey).ToArray());
        foreach (var suffix in new[] { "Maintain", "Delete", "Confirm", "CancelConfirmation", "Lifecycle" })
            Assert.IsNull(PermissionCatalog.Find("Orders.SalesOrder." + suffix));
    }
}
