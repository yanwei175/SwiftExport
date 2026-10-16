using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;
using System.Xml.Linq;

namespace SwiftExport.Client.Wpf.Tests.Features.Products;

[TestClass]
public sealed class ProductsDefaultSupplierTests
{
    /// <summary>兼容编辑列退出产品页后，真实客户筛选和供应商选择仍保留。</summary>
    [TestMethod]
    public void ProductPage_RemovesLegacyColumnsButKeepsCustomerFilter()
    {
        var path = Path.Combine(FindRepositoryRoot(), "SwiftExport.Client.Wpf", "Features", "Products",
            "ProductList", "Views", "ProductsView.xaml");
        var document = XDocument.Load(path);
        var keys = document.Descendants().Attributes()
            .Where(attribute => attribute.Name.LocalName == "SortMemberPath")
            .Select(attribute => attribute.Value).ToArray();
        foreach (var legacyKey in new[] { "DefaultCustomerCode", "DefaultCustomerSku", "DefaultSupplierSku" })
            CollectionAssert.DoesNotContain(keys, legacyKey);
        Assert.IsTrue(document.Descendants().Attributes().Any(attribute =>
            attribute.Name.LocalName == "SelectedItems" && attribute.Value == "{Binding FilterCustomerList}"));
        CollectionAssert.Contains(keys, "DefaultSupplierCode");
    }

    [TestMethod]
    public void DefaultSupplierCodeColumn_ShouldUseAtomicSupplierLookupTemplate()
    {
        var path = Path.Combine(
            FindRepositoryRoot(),
            "SwiftExport.Client.Wpf",
            "Features",
            "Products",
            "ProductList",
            "Views",
            "ProductsView.xaml");
        var document = XDocument.Load(path);
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var column = document.Descendants(presentation + "DataGridTemplateColumn")
            .Single(element => element.Attributes().Any(attribute =>
                attribute.Name.LocalName == "SortMemberPath"
                && string.Equals(attribute.Value, "DefaultSupplierCode", StringComparison.Ordinal)));

        Assert.AreEqual("DefaultSupplierCode", (string?)column.Attribute("SortMemberPath"));
        Assert.IsFalse(column.Attributes().Any(attribute => attribute.Name.LocalName.EndsWith("FieldPath", StringComparison.Ordinal)));
        Assert.AreEqual(
            "DefaultSupplierId",
            column.Attributes().Single(attribute =>
                attribute.Name.LocalName.EndsWith("DataGridFillDownBehavior.Field", StringComparison.Ordinal)).Value);
        Assert.IsTrue(column.Descendants().Any(element => element.Name.LocalName == "SupplierLookupTextBox"));
        Assert.IsTrue(column.Descendants().Attributes().Any(attribute =>
            attribute.Name.LocalName == "SelectionCommittedCommand"
            && attribute.Value.Contains("CommitSupplierCommand", StringComparison.Ordinal)));
        Assert.IsTrue(column.Descendants().Attributes().Any(attribute =>
            attribute.Name.LocalName == "SelectionClearedCommand"
            && attribute.Value.Contains("ClearSupplierCommand", StringComparison.Ordinal)));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SwiftExport-Frontend.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        Assert.Fail("无法定位 SwiftExport 仓库根目录。");
        return string.Empty;
    }
}
