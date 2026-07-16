using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using WpfDropDownButton = Wpf.Ui.Controls.DropDownButton;

namespace SwiftExport.Client.Wpf.Tests.Features.Products.Colors;

[TestClass]
public sealed class ProductColorsMoreMenuTests
{
    [TestMethod]
    public void Xaml_ShouldUseSupportedContextMenuAndPreserveCommands()
    {
        var document = XDocument.Load(GetViewPath());
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace ui = "http://schemas.lepo.co/wpfui/2022/xaml";

        var moreButton = document.Descendants(ui + "DropDownButton")
            .Single(button => button.Descendants(presentation + "TextBlock")
                .Any(text => (string?)text.Attribute("Text") == "更多"));
        var flyoutProperty = moreButton.Element(ui + "DropDownButton.Flyout");
        Assert.IsNotNull(flyoutProperty);
        Assert.IsFalse(flyoutProperty.Descendants(ui + "Flyout").Any());

        var contextMenu = flyoutProperty.Element(presentation + "ContextMenu");
        Assert.IsNotNull(contextMenu);
        StringAssert.Contains(
            (string?)contextMenu.Attribute("DataContext") ?? string.Empty,
            "PlacementTarget.DataContext");

        var menuItems = contextMenu.Elements(presentation + "MenuItem").ToArray();
        CollectionAssert.AreEqual(
            new[] { "导入颜色", "导出颜色", "向下填充（Ctrl+D）", "批量规范化代码" },
            menuItems.Select(item => (string?)item.Attribute("Header")).ToArray());

        var fillDownItem = menuItems.Single(item => (string?)item.Attribute("Header") == "向下填充（Ctrl+D）");
        StringAssert.Contains(
            (string?)fillDownItem.Attribute("Command") ?? string.Empty,
            "GridOperations.FillDownCommand");
        StringAssert.Contains(
            (string?)fillDownItem.Attribute("CommandTarget") ?? string.Empty,
            "ColorsGrid");
    }

    [TestMethod]
    public void DropDownButton_ShouldOpenContextMenuOnMouseRelease()
    {
        StaTest.RunAsync(async () =>
        {
            var contextMenu = new ContextMenu
            {
                Items =
                {
                    new MenuItem { Header = "导入颜色" }
                }
            };
            var button = new WpfDropDownButton
            {
                Width = 100,
                Content = "更多",
                Flyout = contextMenu
            };
            var window = new Window
            {
                Width = 240,
                Height = 120,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None,
                Content = button
            };

            try
            {
                window.Show();
                await Dispatcher.Yield(DispatcherPriority.Loaded);

                var mouseUp = new MouseButtonEventArgs(
                    Mouse.PrimaryDevice,
                    Environment.TickCount,
                    MouseButton.Left)
                {
                    RoutedEvent = UIElement.PreviewMouseLeftButtonUpEvent,
                    Source = button
                };
                button.RaiseEvent(mouseUp);
                await Dispatcher.Yield(DispatcherPriority.Background);

                Assert.IsTrue(button.IsDropDownOpen);
                Assert.IsTrue(contextMenu.IsOpen, "WPF-UI DropDownButton 应通过受支持的 ContextMenu 真正展开菜单。");
                Assert.AreSame(button, contextMenu.PlacementTarget);
            }
            finally
            {
                contextMenu.IsOpen = false;
                window.Close();
            }
        });
    }

    private static string GetViewPath([CallerFilePath] string testFilePath = "") =>
        Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(testFilePath)!,
            "..",
            "..",
            "..",
            "..",
            "SwiftExport.Client.Wpf",
            "Features",
            "Products",
            "Colors",
            "Views",
            "ProductColorsView.xaml"));
}
