using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExportClient.Features.Products.Categories.Views;

namespace SwiftExport.Client.Wpf.Tests.Features.Products;

[TestClass]
[DoNotParallelize]
public sealed class ProductCategoryTreeContextMenuTests
{
    [TestMethod]
    public void Xaml_ShouldAttachContextMenuToTreeViewItemInsteadOfHeaderTemplate()
    {
        var document = XDocument.Load(GetViewPath());
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var itemStyle = document.Descendants(presentation + "Style")
            .Single(element => (string?)element.Attribute(x + "Key") == "ProductCategoryTreeViewItem");
        Assert.IsTrue(itemStyle.Elements(presentation + "Setter")
            .Any(setter => (string?)setter.Attribute("Property") == "ContextMenu"));
        Assert.IsTrue(itemStyle.Elements(presentation + "Setter")
            .Any(setter => (string?)setter.Attribute("Property") == "Tag"));

        var headerTemplate = document.Descendants(presentation + "HierarchicalDataTemplate")
            .Single(element => (string?)element.Attribute(x + "Key") == "ProductCategoryTreeTemplate");
        Assert.IsFalse(headerTemplate.Descendants(presentation + "ContextMenu").Any());

        var treeView = document.Descendants(presentation + "TreeView").Single();
        Assert.AreEqual(
            "OnTreeViewPreviewMouseRightButtonDown",
            (string?)treeView.Attribute("PreviewMouseRightButtonDown"));
    }

    [TestMethod]
    public void RightClickSelection_ShouldSelectNearestTreeViewItem()
    {
        StaTest.RunAsync(() =>
        {
            var text = new TextBlock { Text = "分类" };
            var item = new TreeViewItem { Header = new Border { Child = text } };
            var window = new Window
            {
                Width = 320,
                Height = 160,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None,
                Content = new TreeView { Items = { item } }
            };

            try
            {
                window.Show();
                window.UpdateLayout();

                var method = typeof(ProductCategoryManagerView)
                    .GetMethod("SelectTreeViewItem", BindingFlags.NonPublic | BindingFlags.Static);
                Assert.IsNotNull(method);
                var selected = method.Invoke(null, [text]);

                Assert.AreSame(item, selected);
                Assert.IsTrue(item.IsSelected);
            }
            finally
            {
                window.Close();
            }

            return Task.CompletedTask;
        });
    }

    private static string GetViewPath([CallerFilePath] string testFilePath = "") =>
        Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(testFilePath)!,
            "..",
            "..",
            "..",
            "SwiftExport.Client.Wpf",
            "Features",
            "Products",
            "Categories",
            "Views",
            "ProductCategoryManagerView.xaml"));
}
