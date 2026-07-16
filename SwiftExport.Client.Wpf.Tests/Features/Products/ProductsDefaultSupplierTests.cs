using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;
using System.Xml.Linq;

namespace SwiftExport.Client.Wpf.Tests.Features.Products;

[TestClass]
public sealed class ProductsDefaultSupplierTests
{
    [TestMethod]
    public void DefaultSupplierCodeColumn_ShouldBeReadOnlyAndPointToSupplierProductMaintenance()
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
        var column = document.Descendants(presentation + "DataGridTextColumn")
            .Single(element => element.Attributes().Any(attribute =>
                attribute.Name.LocalName.EndsWith("Key", StringComparison.Ordinal)
                && string.Equals(attribute.Value, "default-supplier-code", StringComparison.Ordinal)));

        Assert.AreEqual("True", (string?)column.Attribute("IsReadOnly"));
        StringAssert.Contains((string?)column.Attribute("Binding") ?? string.Empty, "Mode=OneWay");
        Assert.IsTrue(column.Descendants(presentation + "Setter").Any(setter =>
            string.Equals((string?)setter.Attribute("Property"), "ToolTip", StringComparison.Ordinal)
            && ((string?)setter.Attribute("Value"))?.Contains("供应商产品", StringComparison.Ordinal) == true));
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
