using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExport.Contracts.DTOs.SalesOrders.Workspace;
using SwiftExportClient.Features.Sales.Orders.Components;
using SwiftExportClient.Resources.Controls;

namespace SwiftExport.Client.Wpf.Tests.Features.Sales.Orders;

[TestClass]
[DoNotParallelize]
public sealed class SalesProductLookupTextBoxTests
{
    [TestMethod]
    public void SalesProductLookup_ShouldUseSalesProductIdentityAndSearchFields()
    {
        StaTest.RunAsync(() =>
        {
            var productId = Guid.NewGuid();
            var candidate = new SalesOrderProductLookupDto
            {
                ProductId = productId,
                Sku = "SKU-1",
                CustomerSku = "CUSTOMER-1",
                DescriptionEn = "Complete product description",
                FeaturesEn = "Complete product features",
            };
            var lookup = new SalesProductLookupTextBox
            {
                ItemsSource = new[] { candidate },
            };
            var editor = (LookupTextBox?)lookup.FindName("LookupEditor");

            Assert.IsNotNull(editor);
            Assert.AreEqual(nameof(SalesOrderProductLookupDto.Sku), editor.DisplayMemberPath);
            Assert.AreEqual(nameof(SalesOrderProductLookupDto.ProductId), editor.SelectedValuePath);
            StringAssert.Contains(editor.FilterMemberPaths, nameof(SalesOrderProductLookupDto.CustomerSku));
            StringAssert.Contains(editor.FilterMemberPaths, nameof(SalesOrderProductLookupDto.DescriptionEn));

            editor.SelectedItem = candidate;

            Assert.AreSame(candidate, lookup.SelectedProduct);
            Assert.AreEqual(productId, lookup.SelectedProductId);
            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public void SalesProductLookup_TemplateShouldShowCachedImageCustomerSkuAndCompleteDescription()
    {
        StaTest.RunAsync(() =>
        {
            var lookup = new SalesProductLookupTextBox();
            var editor = (LookupTextBox?)lookup.FindName("LookupEditor");
            Assert.IsNotNull(editor);
            Assert.IsNotNull(editor.ItemTemplate);

            var content = (FrameworkElement)editor.ItemTemplate.LoadContent();
            content.DataContext = new SalesOrderProductLookupDto
            {
                Sku = "SKU-1",
                CustomerSku = "CUSTOMER-1",
                DescriptionEn = "Complete product description",
                MainImage = string.Empty,
            };
            content.Measure(new Size(600, 200));
            content.Arrange(new Rect(content.DesiredSize));

            var images = FindVisualDescendants<CachedImage>(content).ToArray();
            var textBindingPaths = FindVisualDescendants<TextBlock>(content)
                .Select(textBlock => System.Windows.Data.BindingOperations
                    .GetBinding(textBlock, TextBlock.TextProperty)?.Path.Path)
                .ToArray();

            Assert.HasCount(1, images);
            Assert.AreEqual(96, images[0].DecodePixelWidth);
            CollectionAssert.Contains(textBindingPaths, nameof(SalesOrderProductLookupDto.Sku));
            CollectionAssert.Contains(textBindingPaths, nameof(SalesOrderProductLookupDto.CustomerSku));
            CollectionAssert.Contains(textBindingPaths, nameof(SalesOrderProductLookupDto.DescriptionEn));
            return Task.CompletedTask;
        });
    }

    private static IEnumerable<T> FindVisualDescendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in FindVisualDescendants<T>(child))
            {
                yield return descendant;
            }
        }
    }
}
