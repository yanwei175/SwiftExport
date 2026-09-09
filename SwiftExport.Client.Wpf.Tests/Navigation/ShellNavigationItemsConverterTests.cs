using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExportClient.Navigation;
using SwiftExportClient.Shell;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Xml.Linq;
using Wpf.Ui.Controls;

namespace SwiftExport.Client.Wpf.Tests.Navigation;

/// <summary>
/// 验证动态 Shell 导航项使用稳定数据源，不依赖控件挂载后的窗口祖先链。
/// </summary>
[TestClass]
public sealed class ShellNavigationItemsConverterTests
{
    /// <summary>
    /// 动态菜单项应直接绑定 Shell 数据上下文，并保留选中状态和打开命令。
    /// </summary>
    [TestMethod]
    public void Convert_ShouldUseDirectShellBindingSource()
    {
        StaTest.RunAsync(() =>
        {
            var shell = new TestShellBindingSource();
            var model = new ShellNavigationItem(new NavigationMenuItem(
                "sales-orders",
                "销售订单",
                "sales"));
            var converter = new ShellNavigationItemsConverter();

            var result = (IReadOnlyList<NavigationViewItem>)converter.Convert(
                [new[] { model }, shell],
                typeof(IReadOnlyList<NavigationViewItem>),
                null,
                CultureInfo.InvariantCulture);

            var item = result.Single();
            var activeBinding = (MultiBinding)BindingOperations.GetBindingBase(
                item,
                NavigationViewItem.IsActiveProperty)!;
            var routeBinding = (Binding)activeBinding.Bindings[1];
            var paneBinding = (Binding)activeBinding.Bindings[2];
            var commandBinding = BindingOperations.GetBinding(
                item,
                NavigationViewItem.CommandProperty)!;

            Assert.AreSame(shell, routeBinding.Source);
            Assert.AreSame(shell, paneBinding.Source);
            Assert.AreSame(shell, commandBinding.Source);
            Assert.IsNull(routeBinding.RelativeSource);
            Assert.IsNull(paneBinding.RelativeSource);
            Assert.IsNull(commandBinding.RelativeSource);
            Assert.IsTrue(item.IsActive);
            Assert.AreSame(shell.OpenRouteCommand, item.Command);
            return Task.CompletedTask;
        });
    }

    /// <summary>
    /// Shell 页面应同时向转换器提供筛选后的菜单和当前数据上下文。
    /// </summary>
    [TestMethod]
    public void ShellWindow_ShouldProvideMenuAndShellContextToConverter()
    {
        var document = XDocument.Load(GetShellWindowPath());
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace ui = "http://schemas.lepo.co/wpfui/2022/xaml";

        var navigation = document.Descendants(ui + "NavigationView").Single();
        var multiBinding = navigation
            .Element(ui + "NavigationView.MenuItemsSource")!
            .Element(presentation + "MultiBinding")!;
        var bindings = multiBinding.Elements(presentation + "Binding").ToArray();

        StringAssert.Contains(
            (string?)multiBinding.Attribute("Converter") ?? string.Empty,
            "ShellNavigationItemsConverter");
        Assert.AreEqual(2, bindings.Length);
        Assert.AreEqual("FilteredMenuItems", (string?)bindings[0].Attribute("Path"));
        Assert.IsNull(bindings[1].Attribute("Path"));
    }

    private static string GetShellWindowPath()
    {
        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
            {
                var path = Path.Combine(directory.FullName, "SwiftExport.Client.Wpf", "Shell", "ShellWindow.xaml");
                if (File.Exists(path))
                {
                    return path;
                }
            }
        }

        throw new DirectoryNotFoundException("未找到 SwiftExport 仓库根目录。");
    }

    private sealed class TestShellBindingSource
    {
        /// <summary>
        /// 获取测试场景中的当前路由。
        /// </summary>
        public string SelectedRouteKey => "sales-orders";

        /// <summary>
        /// 获取测试场景中的导航栏展开状态。
        /// </summary>
        public bool IsNavigationPaneExpanded => true;

        /// <summary>
        /// 获取测试场景中的路由打开命令。
        /// </summary>
        public ICommand OpenRouteCommand => ApplicationCommands.Open;
    }
}
